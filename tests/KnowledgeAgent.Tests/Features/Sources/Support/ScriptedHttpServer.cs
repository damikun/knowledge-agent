using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Tests.Features.Sources.Support;

internal sealed class ScriptedHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, RequestDelegate> _routes = new(StringComparer.OrdinalIgnoreCase);

    private ScriptedHttpServer(WebApplication app) => _app = app;

    public Uri BaseAddress { get; private set; } = null!;

    public ConcurrentQueue<(string Path, string? UserAgent)> Requests { get; } = new();

    public static async Task<ScriptedHttpServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new ScriptedHttpServer(app);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        server.BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/");
        return server;
    }

    public Uri Url(string path) => new(BaseAddress, path.TrimStart('/'));

    public ScriptedHttpServer Map(string path, RequestDelegate handler)
    {
        _routes["/" + path.TrimStart('/')] = handler;
        return this;
    }

    public ScriptedHttpServer MapRedirect(string path, string location, int status = StatusCodes.Status302Found) =>
        Map(path, context =>
        {
            context.Response.StatusCode = status;
            context.Response.Headers.Location = location;
            return Task.CompletedTask;
        });

    public ScriptedHttpServer MapText(string path, string text, string contentType = "text/plain; charset=utf-8") =>
        Map(path, async context =>
        {
            context.Response.ContentType = contentType;
            await context.Response.WriteAsync(text, context.RequestAborted);
        });

    public int Hits(string path) => Requests.Count(r => string.Equals(r.Path, "/" + path.TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        Requests.Enqueue((path, context.Request.Headers.UserAgent.ToString()));
        if (_routes.TryGetValue(path, out var handler))
        {
            await handler(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
