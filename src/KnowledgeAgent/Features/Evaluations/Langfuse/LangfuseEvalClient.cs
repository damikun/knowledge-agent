using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Infrastructure.Langfuse;

namespace KnowledgeAgent.Features.Evaluations.Langfuse;

// Langfuse v4 has no API to create experiments: it derives them from span attributes on the item traces.
public sealed class LangfuseEvalClient(HttpClient httpClient)
{
    internal const int PageSize = 100;
    internal const int MaxPages = 200;
    private const int MaxErrorDetailLength = 300;

    public bool IsConfigured => httpClient.BaseAddress is not null;

    public static void ConfigureHttpClient(HttpClient client, LangfuseOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!options.IsConfigured
            || !Uri.TryCreate(options.BaseUrl!.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        client.BaseAddress = baseAddress;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.PublicKey!.Trim()}:{options.SecretKey!.Trim()}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<LangfuseDataset?> GetDatasetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var path = "api/public/v2/datasets/" + Uri.EscapeDataString(name);
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
        return ParseDataset(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task<LangfuseDataset> CreateDatasetAsync(string name, string? description, JsonObject? metadata, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        const string path = "api/public/v2/datasets";
        var body = new JsonObject { ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description))
        {
            body["description"] = description;
        }

        if (metadata is not null)
        {
            body["metadata"] = metadata.DeepClone();
        }

        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post, path, cancellationToken);
        return ParseDataset(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task<IReadOnlyList<LangfuseDataset>> ListDatasetsAsync(CancellationToken cancellationToken = default)
    {
        var datasets = new List<LangfuseDataset>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"api/public/v2/datasets?page={page}&limit={PageSize}");
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
            var json = await ReadJsonAsync(response, cancellationToken);
            var data = json?["data"] as JsonArray ?? [];
            datasets.AddRange(data.OfType<JsonObject>().Select(ParseDataset));
            if (data.Count == 0 || page >= TotalPages(json))
            {
                break;
            }
        }

        return datasets;
    }

    public async Task<IReadOnlyList<LangfuseDatasetItem>> ListItemsAsync(string datasetName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);
        var items = new List<LangfuseDatasetItem>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"api/public/dataset-items?datasetName={Uri.EscapeDataString(datasetName)}&page={page}&limit={PageSize}");
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
            var json = await ReadJsonAsync(response, cancellationToken);
            var data = json?["data"] as JsonArray ?? [];
            items.AddRange(data.OfType<JsonObject>().Select(ParseItem));
            if (data.Count == 0 || page >= TotalPages(json))
            {
                break;
            }
        }

        return items;
    }

    public async Task<LangfuseDatasetItem> UpsertItemAsync(LangfuseDatasetItemUpsert item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        const string path = "api/public/dataset-items";
        var body = new JsonObject
        {
            ["datasetName"] = item.DatasetName,
            ["id"] = item.Id,
            ["input"] = item.Input.DeepClone(),
            ["expectedOutput"] = item.ExpectedOutput?.DeepClone(),
            ["metadata"] = item.Metadata?.DeepClone(),
            ["status"] = item.Status,
        };

        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post, path, cancellationToken);
        return ParseItem(await ReadJsonAsync(response, cancellationToken) as JsonObject ?? new JsonObject());
    }

    public async Task<LangfuseScoreBatchResult> CreateScoresAsync(IReadOnlyList<LangfuseScore> scores, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
        {
            return new LangfuseScoreBatchResult(0, 0, []);
        }

        const string path = "api/public/scores";
        var body = new JsonArray(scores.Select(score => (JsonNode)ToJson(score)).ToArray());
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post, path, cancellationToken);

        // Langfuse answers 202 when every score was accepted and 207 with per-score errors otherwise.
        if (response.StatusCode != HttpStatusCode.MultiStatus)
        {
            return new LangfuseScoreBatchResult(scores.Count, 0, []);
        }

        var json = await ReadJsonAsync(response, cancellationToken);
        var errors = (json?["errors"] as JsonArray ?? [])
            .Select(error => error?["message"]?.GetValue<string>() ?? error?.ToJsonString() ?? "unknown error")
            .ToList();
        return new LangfuseScoreBatchResult(
            json?["accepted"]?.GetValue<int>() ?? 0,
            json?["rejected"]?.GetValue<int>() ?? errors.Count,
            errors);
    }

    public async Task<string?> GetProjectIdAsync(CancellationToken cancellationToken = default)
    {
        const string path = "api/public/projects";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Get, path, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        // Project-scoped API keys see exactly one project.
        return (json?["data"] as JsonArray)?.OfType<JsonObject>().Select(project => project["id"]?.GetValue<string>()).FirstOrDefault(id => !string.IsNullOrEmpty(id));
    }

    internal static JsonObject ToJson(LangfuseScore score)
    {
        var json = new JsonObject
        {
            ["id"] = score.Id,
            ["name"] = score.Name,
            ["value"] = score.Value,
            ["dataType"] = score.DataType,
        };
        Add(json, "traceId", score.TraceId);
        Add(json, "sessionId", score.SessionId);
        Add(json, "observationId", score.ObservationId);
        Add(json, "datasetRunId", score.DatasetRunId);
        Add(json, "comment", score.Comment);
        Add(json, "environment", score.Environment);
        if (score.Metadata is not null)
        {
            json["metadata"] = score.Metadata.DeepClone();
        }

        return json;

        static void Add(JsonObject target, string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                target[key] = value;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            throw new LangfuseEvalException(
                "Langfuse is not configured: Langfuse:BaseUrl (an absolute URL), Langfuse:PublicKey and Langfuse:SecretKey are required.");
        }

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LangfuseEvalException($"Langfuse is unreachable ({method} {path.Split('?')[0]}): {ex.Message}", null, ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (text.Length > 0)
            {
                try
                {
                    if (JsonNode.Parse(text) is JsonObject error && error["message"] is JsonValue message && message.TryGetValue<string>(out var value))
                    {
                        text = value;
                    }
                }
                catch (JsonException)
                {
                }

                text = text.ReplaceLineEndings(" ");
                detail = text.Length <= MaxErrorDetailLength ? text : text[..MaxErrorDetailLength] + "…";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
        }

        throw new LangfuseEvalException(
            $"Langfuse returned {(int)response.StatusCode} ({response.StatusCode}) for {method} {path.Split('?')[0]}{(detail is null ? "" : ": " + detail)}",
            response.StatusCode);
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return stream.CanSeek && stream.Length == 0 ? null : await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new LangfuseEvalException("Langfuse returned a response that is not valid JSON.", response.StatusCode, ex);
        }
    }

    private static int TotalPages(JsonNode? json) =>
        json?["meta"]?["totalPages"] is JsonValue value && value.TryGetValue<int>(out var pages) ? pages : 1;

    private static LangfuseDataset ParseDataset(JsonNode? json) => new(
        Text(json?["id"]) ?? throw new LangfuseEvalException("Langfuse returned a dataset without an id."),
        Text(json?["name"]) ?? string.Empty,
        Text(json?["description"]),
        json?["metadata"]?.DeepClone());

    private static LangfuseDatasetItem ParseItem(JsonObject json) => new(
        Text(json["id"]) ?? string.Empty,
        Text(json["datasetName"]),
        Text(json["datasetId"]),
        Text(json["status"]) ?? "ACTIVE",
        json["input"]?.DeepClone(),
        json["expectedOutput"]?.DeepClone(),
        json["metadata"]?.DeepClone(),
        Text(json["version"]),
        Text(json["sourceTraceId"]));

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}

public sealed record LangfuseDataset(string Id, string Name, string? Description, JsonNode? Metadata);

public sealed record LangfuseDatasetItem(
    string Id,
    string? DatasetName,
    string? DatasetId,
    string Status,
    JsonNode? Input,
    JsonNode? ExpectedOutput,
    JsonNode? Metadata,
    string? Version,
    string? SourceTraceId)
{
    public bool IsActive => string.Equals(Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
}

public sealed record LangfuseDatasetItemUpsert(string DatasetName, string Id, JsonNode Input, JsonNode? ExpectedOutput, JsonNode? Metadata, string Status = "ACTIVE");

public sealed record LangfuseScore(
    string Id,
    string Name,
    double Value,
    string? TraceId = null,
    string? ObservationId = null,
    string? DatasetRunId = null,
    string? Comment = null,
    string? Environment = null,
    JsonObject? Metadata = null,
    string DataType = "NUMERIC",
    string? SessionId = null);

public sealed record LangfuseScoreBatchResult(int Accepted, int Rejected, IReadOnlyList<string> Errors);

public sealed class LangfuseEvalException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
