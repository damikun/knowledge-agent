using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Support;

public static class TestJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record ProblemDocument(
    int? Status,
    string? Title,
    string? Detail,
    string? Type,
    string? Instance,
    Dictionary<string, string[]>? Errors,
    string? Code = null)
{
    // Case-insensitive because modules may report "url" or "Url".
    public Dictionary<string, string[]> FieldErrors =>
        Errors is null
            ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string[]>(Errors, StringComparer.OrdinalIgnoreCase);
}

public static class ApiClient
{
    public static readonly TimeSpan IngestionTimeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned: {body}");
        return JsonSerializer.Deserialize<T>(body, TestJson.Options)
            ?? throw new ShouldAssertException($"Empty JSON body from {response.RequestMessage?.RequestUri}.");
    }

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        return await response.ReadJsonAsync<T>();
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, TestJson.Options, Ct);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, TestJson.Options, Ct);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsJsonAsync(url, body, TestJson.Options, Ct);

    public static async Task<ProblemDocument> ReadProblemAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned: {body}");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);
        var problem = JsonSerializer.Deserialize<ProblemDocument>(body, TestJson.Options).ShouldNotBeNull();
        problem.Status.ShouldBe((int)expected);
        body.ShouldNotContain("   at ", Case.Sensitive, "problem details must not leak stack traces");
        return problem;
    }

    public static async Task<SourceDto> AddTextSourceAsync(this KnowledgeAgentApp app, string title, string content, string? format = null)
    {
        using var response = await app.Client.PostJsonAsync("/api/sources/text", new AddTextSourceRequest(title, content, format));
        return await response.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted);
    }

    public static async Task<SourceDto> AddReadyTextSourceAsync(this KnowledgeAgentApp app, string title, string content)
    {
        var created = await app.AddTextSourceAsync(title, content);
        return await app.WaitForSourceAsync(created.Id);
    }

    public static async Task<SourceDto> WaitForSourceAsync(
        this KnowledgeAgentApp app,
        Guid id,
        Func<SourceDto, bool>? until = null,
        TimeSpan? timeout = null)
    {
        until ??= source => source.Status == "ready";
        var limit = timeout ?? IngestionTimeout;
        var stopwatch = Stopwatch.StartNew();
        SourceDto? last = null;
        while (stopwatch.Elapsed < limit)
        {
            last = await app.Client.GetJsonAsync<SourceDto>($"/api/sources/{id}");
            if (until(last))
            {
                return last;
            }

            if (last.Status == "failed")
            {
                throw new ShouldAssertException(
                    $"Source {id} failed to ingest: {last.Error}{Environment.NewLine}{app.Logs.Dump()}");
            }

            await Task.Delay(100, Ct);
        }

        throw new ShouldAssertException(
            $"Source {id} did not reach the expected state within {limit.TotalSeconds:0}s (last status: {last?.Status}, "
            + $"error: {last?.Error}).{Environment.NewLine}{app.Logs.Dump()}");
    }

    public static async Task<SearchHitDto[]> SearchAsync(
        this KnowledgeAgentApp app,
        string query,
        int? topK = null,
        IReadOnlyList<Guid>? sourceIds = null)
    {
        using var response = await app.Client.PostJsonAsync("/api/search", new SearchRequest(query, topK, sourceIds));
        return await response.ReadJsonAsync<SearchHitDto[]>();
    }

    public static async Task<ChatStream> ChatAsync(this KnowledgeAgentApp app, ChatRequestDto request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, options: TestJson.Options),
        };
        message.Headers.Accept.Add(SseReader.AcceptHeader);

        using var response = await app.Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, Ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            throw new ShouldAssertException($"POST /api/chat returned {(int)response.StatusCode}: {body}{Environment.NewLine}{app.Logs.Dump()}");
        }

        var events = await SseReader.ReadAllAsync(response, Ct);
        return new ChatStream(
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.CacheControl?.ToString(),
            response.Headers.TryGetValues("X-Accel-Buffering", out var buffering) ? string.Join(",", buffering) : null,
            events);
    }

    public static async Task<bool> EventuallyAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                return false;
            }

            await Task.Delay(100, Ct);
        }

        return true;
    }

    // Letters and digits only, so the fake embeddings treat it as a single token.
    public static string UniqueWord(string prefix = "zq") => prefix + Guid.NewGuid().ToString("N")[..10];
}

public sealed class ChatStream(string? contentType, string? cacheControl, string? accelBuffering, IReadOnlyList<SseEvent> events)
{
    public string? ContentType { get; } = contentType;
    public string? CacheControl { get; } = cacheControl;
    public string? AccelBuffering { get; } = accelBuffering;
    public IReadOnlyList<SseEvent> Events { get; } = events;
    public List<string> Types { get; } = events.Select(e => e.Event).ToList();

    public IReadOnlyList<T> All<T>(string eventType) =>
        Events.Where(e => e.Event == eventType).Select(e => e.Deserialize<T>()).ToList();

    public T First<T>(string eventType) =>
        All<T>(eventType).FirstOrDefault() ?? throw new ShouldAssertException($"No '{eventType}' event.{Environment.NewLine}{Describe()}");

    public ConversationEvent Conversation => First<ConversationEvent>(ChatEventTypes.Conversation);

    public Guid ConversationId => Conversation.ConversationId;

    public DoneEvent Done =>
        All<DoneEvent>(ChatEventTypes.Done).SingleOrDefault() ?? throw new ShouldAssertException($"No single 'done' event.{Environment.NewLine}{Describe()}");

    public IReadOnlyList<ErrorEvent> Errors => All<ErrorEvent>(ChatEventTypes.Error);

    public AgentStartedEvent OrchestratorStarted =>
        All<AgentStartedEvent>(ChatEventTypes.AgentStarted).FirstOrDefault(e => e.Agent == "orchestrator")
        ?? throw new ShouldAssertException($"No orchestrator 'agent-started' event.{Environment.NewLine}{Describe()}");

    public string Text => string.Concat(All<TextDeltaEvent>(ChatEventTypes.TextDelta).Where(e => e.Agent == "orchestrator").Select(e => e.Text));

    public string Describe() => string.Join(Environment.NewLine, Events.Select(e => $"{e.Event}: {e.Data}"));
}
