using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal sealed record CapturedRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonObject Json => JsonNode.Parse(Body)?.AsObject() ?? throw new InvalidOperationException("Request body is not a JSON object.");

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

internal sealed record CannedResponse(int Status, string ContentType, string Body)
{
    public static CannedResponse Json(string body, int status = 200) => new(status, "application/json", body);

    public static CannedResponse EventStream(string body) => new(200, "text/event-stream", body);
}

internal sealed class CapturingHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    private CapturingHttpServer(WebApplication app) => _app = app;

    public Uri BaseAddress { get; private set; } = null!;

    public Func<CapturedRequest, CannedResponse> Responder { get; set; } = _ => CannedResponse.Json("{}");

    public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    public CapturedRequest LastRequest => _requests.LastOrDefault() ?? throw new InvalidOperationException("No request was received.");

    public static async Task<CapturingHttpServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new CapturingHttpServer(app);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        server.BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/");
        return server;
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var request = new CapturedRequest(context.Request.Method, context.Request.Path + context.Request.QueryString, headers, body);
        _requests.Enqueue(request);

        var response = Responder(request);
        context.Response.StatusCode = response.Status;
        context.Response.ContentType = response.ContentType;
        await context.Response.WriteAsync(response.Body, context.RequestAborted);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
