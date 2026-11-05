using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.TestKit;

public sealed class FakeLangfuseServer : IAsyncDisposable
{
    public const string DefaultPublicKey = "pk-lf-test";
    public const string DefaultSecretKey = "sk-lf-test";
    public const string ProjectId = "fake-project";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, List<StoredPrompt>> _prompts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _requestLog = new();
    private readonly ConcurrentDictionary<string, JsonObject> _datasets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, JsonObject> _datasetItems = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<JsonObject> _scores = new();
    private int _itemWrites;
    private readonly string _expectedAuthorization;
    private int _traceExports;

    private FakeLangfuseServer(WebApplication app, string publicKey, string secretKey)
    {
        _app = app;
        PublicKey = publicKey;
        SecretKey = secretKey;
        _expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
    }

    public Uri BaseAddress { get; private set; } = null!;
    public string PublicKey { get; }
    public string SecretKey { get; }

    public bool Unavailable { get; set; }

    public int TraceExportCount => Volatile.Read(ref _traceExports);

    public IReadOnlyDictionary<string, JsonObject> Datasets => _datasets;

    public IReadOnlyList<JsonObject> Scores => _scores.ToArray();

    public int DatasetItemWrites => Volatile.Read(ref _itemWrites);

    public Func<JsonObject, string?>? RejectScore { get; set; }

    public string AddDataset(string name, string? description = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return _datasets.GetOrAdd(name, _ => new JsonObject
        {
            ["id"] = "ds-" + Guid.NewGuid().ToString("N")[..12],
            ["name"] = name,
            ["description"] = description,
            ["metadata"] = null,
            ["projectId"] = ProjectId,
            ["createdAt"] = now,
            ["updatedAt"] = now,
        })["id"]!.GetValue<string>();
    }

    public void AddDatasetItem(string datasetName, string id, JsonNode input, JsonNode? expectedOutput = null, JsonNode? metadata = null, string status = "ACTIVE")
    {
        var dataset = _datasets[datasetName];
        var now = DateTimeOffset.UtcNow.ToString("O");
        var body = new JsonObject
        {
            ["input"] = input.DeepClone(),
            ["expectedOutput"] = expectedOutput?.DeepClone(),
            ["metadata"] = metadata?.DeepClone(),
            ["status"] = status,
        };
        _datasetItems[id] = NewItem(id, dataset, body, now, now);
    }

    public IReadOnlyDictionary<string, JsonObject> DatasetItems(string datasetName) =>
        _datasetItems.Values
            .Where(item => item["datasetName"]?.GetValue<string>() == datasetName)
            .ToDictionary(item => item["id"]!.GetValue<string>(), item => item, StringComparer.Ordinal);

    public IReadOnlyCollection<string> RequestLog => _requestLog.ToArray();

    public static async Task<FakeLangfuseServer> StartAsync(string publicKey = DefaultPublicKey, string secretKey = DefaultSecretKey)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new FakeLangfuseServer(app, publicKey, secretKey);
        server.MapRoutes();
        await app.StartAsync();
        server.BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/");
        return server;
    }

    public int SetTextPrompt(string name, string prompt, JsonObject? config = null, params string[] labels) =>
        AddVersion(name, "text", JsonValue.Create(prompt), config, labels.Length == 0 ? ["production"] : labels, []);

    public int SetCommittedTextPrompt(string name, string prompt, string? commitMessage) =>
        AddVersion(name, "text", JsonValue.Create(prompt), null, ["production"], [], commitMessage);

    public int SetChatPrompt(string name, IEnumerable<(string Role, string Content)> messages, JsonObject? config = null, params string[] labels)
    {
        var array = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray());
        return AddVersion(name, "chat", array, config, labels.Length == 0 ? ["production"] : labels, []);
    }

    public IReadOnlyList<int> Versions(string name) =>
        _prompts.TryGetValue(name, out var versions) ? versions.Select(v => v.Version).ToList() : [];

    public bool HasPrompt(string name) => _prompts.ContainsKey(name);

    public IReadOnlyList<string> LabelsOf(string name, int version)
    {
        if (!_prompts.TryGetValue(name, out var versions))
        {
            return [];
        }

        lock (versions)
        {
            return versions.FirstOrDefault(v => v.Version == version)?.Labels.ToList() ?? [];
        }
    }

    private int AddVersion(
        string name,
        string type,
        JsonNode? prompt,
        JsonObject? config,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> tags,
        string? commitMessage = null)
    {
        var versions = _prompts.GetOrAdd(name, _ => []);
        lock (versions)
        {
            var version = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1;
            var newLabels = labels.Where(l => l != "latest").Distinct().ToList();
            foreach (var existing in versions)
            {
                existing.Labels.RemoveAll(l => l == "latest" || newLabels.Contains(l));
            }

            newLabels.Add("latest");
            versions.Add(new StoredPrompt(version, type, prompt?.DeepClone(), (JsonObject?)config?.DeepClone() ?? new JsonObject(), newLabels, tags.ToList(), DateTimeOffset.UtcNow, commitMessage));
            return version;
        }
    }

    private void MapRoutes()
    {
        _app.Use(async (context, next) =>
        {
            _requestLog.Enqueue($"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
            if (Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            if (context.Request.Path != "/api/public/health"
                && context.Request.Headers.Authorization.ToString() != _expectedAuthorization)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid credentials. Confirm that you've configured the correct host.", error = "UnauthorizedError" });
                return;
            }

            await next(context);
        });

        _app.MapGet("/api/public/health", () => Results.Json(new { status = "OK", version = "fake" }));
        _app.MapGet("/api/public/projects", () => Results.Json(new { data = new[] { new { id = ProjectId, name = "Fake project" } } }));

        _app.MapPost("/api/public/otel/v1/traces", async (HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            Interlocked.Increment(ref _traceExports);
            return Results.Ok();
        });

        _app.MapGet("/api/public/v2/datasets", (int? page, int? limit) =>
            Paged(_datasets.Values.OrderBy(dataset => dataset["name"]!.GetValue<string>(), StringComparer.Ordinal), page, limit));

        _app.MapGet("/api/public/v2/datasets/{name}", (string name) =>
            _datasets.TryGetValue(name, out var dataset)
                ? Results.Json(dataset)
                : Results.Json(new { error = "LangfuseNotFoundError", message = "Dataset not found" }, statusCode: StatusCodes.Status404NotFound));

        _app.MapPost("/api/public/v2/datasets", async (HttpRequest request) =>
        {
            var body = await JsonNode.ParseAsync(request.Body) as JsonObject;
            var name = body?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { message = "name is required" });
            }

            var dataset = _datasets.GetOrAdd(name, _ => new JsonObject
            {
                ["id"] = "ds-" + Guid.NewGuid().ToString("N")[..12],
                ["name"] = name,
                ["description"] = body!["description"]?.DeepClone(),
                ["metadata"] = body["metadata"]?.DeepClone(),
                ["projectId"] = ProjectId,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            });
            return Results.Json(dataset);
        });

        _app.MapGet("/api/public/dataset-items", (string? datasetName, int? page, int? limit) =>
            Paged(
                _datasetItems.Values
                    .Where(item => datasetName is null || item["datasetName"]?.GetValue<string>() == datasetName)
                    .OrderBy(item => item["createdAt"]!.GetValue<string>(), StringComparer.Ordinal)
                    .ThenBy(item => item["id"]!.GetValue<string>(), StringComparer.Ordinal),
                page,
                limit));

        _app.MapPost("/api/public/dataset-items", async (HttpRequest request) =>
        {
            var body = await JsonNode.ParseAsync(request.Body) as JsonObject;
            var datasetName = body?["datasetName"]?.GetValue<string>();
            if (datasetName is null || !_datasets.TryGetValue(datasetName, out var dataset))
            {
                return Results.Json(new { error = "LangfuseNotFoundError", message = "Dataset not found" }, statusCode: StatusCodes.Status404NotFound);
            }

            var id = body!["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("D");
            var now = DateTimeOffset.UtcNow.ToString("O");
            var item = _datasetItems.AddOrUpdate(
                id,
                _ => NewItem(id, dataset, body, now, now),
                (_, existing) => NewItem(id, dataset, body, existing["createdAt"]!.GetValue<string>(), now));
            Interlocked.Increment(ref _itemWrites);
            return Results.Json(item);
        });

        _app.MapPost("/api/public/scores", async (HttpRequest request) =>
        {
            var body = await JsonNode.ParseAsync(request.Body) as JsonArray;
            if (body is null)
            {
                return Results.BadRequest(new { message = "Expected an array of scores" });
            }

            var errors = new JsonArray();
            var accepted = 0;
            foreach (var score in body.OfType<JsonObject>())
            {
                if (RejectScore?.Invoke(score) is { } reason)
                {
                    errors.Add(new JsonObject { ["message"] = reason });
                    continue;
                }

                _scores.Enqueue((JsonObject)score.DeepClone());
                accepted++;
            }

            return errors.Count == 0
                ? Results.Json(new { message = "Accepted" }, statusCode: StatusCodes.Status202Accepted)
                : Results.Json(new JsonObject { ["accepted"] = accepted, ["rejected"] = errors.Count, ["errors"] = errors }, statusCode: StatusCodes.Status207MultiStatus);
        });

        _app.MapGet("/api/public/v2/prompts/{name}", (string name, string? label, int? version) =>
        {
            if (!_prompts.TryGetValue(name, out var versions))
            {
                return NotFound();
            }

            StoredPrompt? match;
            lock (versions)
            {
                match = version is not null
                    ? versions.FirstOrDefault(v => v.Version == version)
                    : versions.FirstOrDefault(v => v.Labels.Contains(label ?? "production"));
            }

            return match is null ? NotFound() : Results.Json(ToJson(name, match));
        });

        _app.MapGet("/api/public/v2/prompts", (int? page, int? limit, string? name) =>
        {
            var items = _prompts
                .Where(p => name is null || p.Key == name)
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p =>
                {
                    lock (p.Value)
                    {
                        return new JsonObject
                        {
                            ["name"] = p.Key,
                            ["type"] = p.Value[^1].Type,
                            ["versions"] = new JsonArray(p.Value.Select(v => (JsonNode)v.Version).ToArray()),
                            ["labels"] = new JsonArray(p.Value.SelectMany(v => v.Labels).Distinct().Select(l => (JsonNode)l).ToArray()),
                            ["tags"] = new JsonArray(),
                            ["lastUpdatedAt"] = p.Value.Max(v => v.CreatedAt).ToString("O"),
                            ["lastConfig"] = p.Value[^1].Config.DeepClone(),
                        };
                    }
                })
                .ToList();
            // Langfuse v4 quirks: the default page size is 10 and meta is repeated as "pagination".
            var size = limit ?? 10;
            var pageNumber = page ?? 1;
            var data = new JsonArray(items.Skip((pageNumber - 1) * size).Take(size).Select(i => (JsonNode)i).ToArray());
            var meta = new JsonObject
            {
                ["page"] = pageNumber,
                ["limit"] = size,
                ["totalItems"] = items.Count,
                ["totalPages"] = (int)Math.Ceiling(items.Count / (double)size),
            };
            return Results.Json(new JsonObject
            {
                ["data"] = data,
                ["meta"] = meta,
                ["pagination"] = meta.DeepClone(),
            });
        });

        _app.MapPost("/api/public/v2/prompts", async (HttpRequest request) =>
        {
            var body = await JsonNode.ParseAsync(request.Body) as JsonObject;
            var name = body?["name"]?.GetValue<string>();
            if (body is null || string.IsNullOrWhiteSpace(name) || body["prompt"] is null)
            {
                return Results.BadRequest(new { message = "name and prompt are required" });
            }

            var type = body["type"]?.GetValue<string>() ?? "text";
            var labels = body["labels"]?.AsArray().Select(l => l!.GetValue<string>()).ToList() ?? [];
            var tags = body["tags"]?.AsArray().Select(t => t!.GetValue<string>()).ToList() ?? [];
            var version = AddVersion(name, type, body["prompt"], body["config"] as JsonObject, labels, tags, body["commitMessage"]?.GetValue<string>());
            var stored = _prompts[name].First(v => v.Version == version);
            return Results.Json(ToJson(name, stored), statusCode: StatusCodes.Status201Created);
        });

        _app.MapMethods("/api/public/v2/prompts/{name}/versions/{version:int}", ["PATCH"], async (string name, int version, HttpRequest request) =>
        {
            if (!_prompts.TryGetValue(name, out var versions))
            {
                return NotFound();
            }

            var body = await JsonNode.ParseAsync(request.Body);
            var newLabels = body?["newLabels"]?.AsArray().Select(l => l!.GetValue<string>()).ToList() ?? [];
            lock (versions)
            {
                var target = versions.FirstOrDefault(v => v.Version == version);
                if (target is null)
                {
                    return NotFound();
                }

                foreach (var other in versions)
                {
                    other.Labels.RemoveAll(newLabels.Contains);
                }

                target.Labels.AddRange(newLabels);
                return Results.Json(ToJson(name, target));
            }
        });
    }

    private static JsonObject NewItem(string id, JsonObject dataset, JsonObject body, string createdAt, string version) => new()
    {
        ["id"] = id,
        ["status"] = body["status"]?.GetValue<string>() ?? "ACTIVE",
        ["input"] = body["input"]?.DeepClone(),
        ["expectedOutput"] = body["expectedOutput"]?.DeepClone(),
        ["metadata"] = body["metadata"]?.DeepClone(),
        ["sourceTraceId"] = null,
        ["sourceObservationId"] = null,
        ["datasetId"] = dataset["id"]!.GetValue<string>(),
        ["datasetName"] = dataset["name"]!.GetValue<string>(),
        ["createdAt"] = createdAt,
        ["updatedAt"] = version,
        ["version"] = version,
        ["mediaReferences"] = new JsonArray(),
    };

    private static IResult Paged(IEnumerable<JsonObject> source, int? page, int? limit)
    {
        var all = source.ToList();
        var size = limit ?? 50;
        var number = page ?? 1;
        return Results.Json(new JsonObject
        {
            ["data"] = new JsonArray(all.Skip((number - 1) * size).Take(size).Select(item => (JsonNode)item.DeepClone()).ToArray()),
            ["meta"] = new JsonObject
            {
                ["page"] = number,
                ["limit"] = size,
                ["totalItems"] = all.Count,
                ["totalPages"] = (int)Math.Ceiling(all.Count / (double)size),
            },
        });
    }

    private static IResult NotFound() =>
        Results.Json(new { error = "LangfuseNotFoundError", message = "Prompt not found" }, statusCode: StatusCodes.Status404NotFound);

    private static JsonObject ToJson(string name, StoredPrompt prompt) => new()
    {
        ["id"] = $"{name}-{prompt.Version}",
        ["name"] = name,
        ["version"] = prompt.Version,
        ["type"] = prompt.Type,
        ["prompt"] = prompt.Prompt?.DeepClone(),
        ["config"] = prompt.Config.DeepClone(),
        ["labels"] = new JsonArray(prompt.Labels.Select(l => (JsonNode)l).ToArray()),
        ["tags"] = new JsonArray(prompt.Tags.Select(t => (JsonNode)t).ToArray()),
        ["commitMessage"] = prompt.CommitMessage,
    };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed record StoredPrompt(
        int Version,
        string Type,
        JsonNode? Prompt,
        JsonObject Config,
        List<string> Labels,
        List<string> Tags,
        DateTimeOffset CreatedAt,
        string? CommitMessage);
}
