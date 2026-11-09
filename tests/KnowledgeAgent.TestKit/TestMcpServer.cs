using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeAgent.TestKit;

// A real MCP server over Streamable HTTP, built with the official SDK. lookup_order reads, cancel_order writes,
// report_problem returns an error and broken throws.
public sealed class TestMcpServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestMcpServer(WebApplication app) => _app = app;

    public Uri Url { get; private set; } = null!;

    public string? RequiredToken { get; set; }

    public string? RequiredHeader { get; set; }

    public ConcurrentQueue<string> AuthorizationHeaders { get; } = new();

    public ConcurrentQueue<(string Tool, string Arguments)> Calls { get; } = new();

    // The caller entry of each lookup_order call's _meta, as JSON ("null" without one).
    public ConcurrentQueue<string> LookupCaller { get; } = new();

    // The idempotency key in each lookup_order and cancel_order call's _meta.
    public ConcurrentQueue<(string Tool, string? Key)> IdempotencyKeys { get; } = new();

    private int _rejectCalls;
    private int _dropResponses;

    // The next tool calls are answered with HTTP 404 before the tool runs, as for an expired session.
    public void RejectNextCalls(int count) => Interlocked.Exchange(ref _rejectCalls, count);

    // The next tool calls run, then the connection drops before the client gets the result.
    public void DropNextResponses(int count) => Interlocked.Exchange(ref _dropResponses, count);

    public static async Task<TestMcpServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        TestMcpServer? server = null;
        builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "test-shop", Version = "2.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools(
            [
                McpServerTool.Create(
                    [Description("Looks up an order")] (string orderId, RequestContext<CallToolRequestParams> request) =>
                    {
                        server!.LookupCaller.Enqueue(request.Params?.Meta?["knowledge-agent/caller"]?.ToJsonString() ?? "null");
                        server.IdempotencyKeys.Enqueue(("lookup_order", request.Params?.Meta?["knowledge-agent/idempotency-key"]?.GetValue<string>()));
                        server.Calls.Enqueue(("lookup_order", orderId));
                        return $"Order {orderId}: shipped on 2026-11-01.";
                    },
                    new McpServerToolCreateOptions { Name = "lookup_order", Title = "Look up an order", ReadOnly = true }),
                McpServerTool.Create(
                    [Description("Cancels an order")] (string orderId, RequestContext<CallToolRequestParams> request) =>
                    {
                        server!.IdempotencyKeys.Enqueue(("cancel_order", request.Params?.Meta?["knowledge-agent/idempotency-key"]?.GetValue<string>()));
                        server.Calls.Enqueue(("cancel_order", orderId));
                        return $"Order {orderId} is cancelled.";
                    },
                    new McpServerToolCreateOptions { Name = "cancel_order", Title = "Cancel an order", Destructive = true }),
                McpServerTool.Create(
                    () => new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "The shop is closed." }],
                        IsError = true,
                    },
                    new McpServerToolCreateOptions { Name = "report_problem", ReadOnly = true }),
                McpServerTool.Create(
                    string () => throw new InvalidOperationException("secret internal detail"),
                    new McpServerToolCreateOptions { Name = "broken", ReadOnly = true }),
            ]);

        var app = builder.Build();
        server = new TestMcpServer(app);
        app.Use(async (context, next) =>
        {
            var header = server.RequiredHeader ?? "Authorization";
            var value = context.Request.Headers[header].ToString();
            server.AuthorizationHeaders.Enqueue(value);
            var expected = server.RequiredHeader is null ? "Bearer " + server.RequiredToken : server.RequiredToken;
            if (server.RequiredToken is not null && value != expected)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.Use(async (context, next) =>
        {
            if (!await IsToolCallAsync(context.Request))
            {
                await next(context);
                return;
            }

            if (TakeOne(ref server._rejectCalls))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (TakeOne(ref server._dropResponses))
            {
                context.Response.Body = new MemoryStream();
                await next(context);
                context.Abort();
                return;
            }

            await next(context);
        });
        app.MapMcp("/mcp");
        await app.StartAsync();
        server.Url = new Uri(app.Urls.First().TrimEnd('/') + "/mcp");
        return server;
    }

    private static async Task<bool> IsToolCallAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return body.Contains("\"tools/call\"", StringComparison.Ordinal);
    }

    private static bool TakeOne(ref int counter)
    {
        while (true)
        {
            var current = Volatile.Read(ref counter);
            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref counter, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
