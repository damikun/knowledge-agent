using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.TestKit;

public sealed class TestHttpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, (string ContentType, byte[] Body, int Status)> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);

    private TestHttpServer(WebApplication app) => _app = app;

    public Uri BaseAddress { get; private set; } = null!;

    public static async Task<TestHttpServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new TestHttpServer(app);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        server.BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/");
        return server;
    }

    public Uri Url(string path) => new(BaseAddress, path.TrimStart('/'));

    public TestHttpServer AddHtml(string path, string html) => Add(path, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));

    public TestHttpServer AddText(string path, string text, string contentType = "text/plain; charset=utf-8") =>
        Add(path, contentType, Encoding.UTF8.GetBytes(text));

    public TestHttpServer Add(string path, string contentType, byte[] body, int status = 200)
    {
        _routes["/" + path.TrimStart('/')] = (contentType, body, status);
        return this;
    }

    public int Hits(string path) => _hits.TryGetValue("/" + path.TrimStart('/'), out var count) ? count : 0;

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        _hits.AddOrUpdate(path, 1, (_, count) => count + 1);
        if (!_routes.TryGetValue(path, out var route))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.StatusCode = route.Status;
        context.Response.ContentType = route.ContentType;
        await context.Response.Body.WriteAsync(route.Body);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
