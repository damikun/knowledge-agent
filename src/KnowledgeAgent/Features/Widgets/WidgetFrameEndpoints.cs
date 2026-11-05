using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Widgets;

internal static class WidgetFrameEndpoints
{
    public static RouteGroupBuilder MapWidgetFrameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup(WidgetUrls.Base).AllowAnonymous().ExcludeFromDescription();
        group.MapGet("/frame/{key}", FrameAsync);
        group.MapGet("/preview", Preview);
        group.MapGet("/media/{id:guid}", MediaAsync);
        group.MapGet("/{**path}", Static);
        return group;
    }

    private static async Task<IResult> FrameAsync(string key, HttpResponse response, VisitorService visitors, WidgetAssetFiles files, CancellationToken cancellationToken)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-cache";
        if (await visitors.FindWidgetAsync(key, cancellationToken) is not { } widget)
        {
            return Results.Content(Page("This chat is not available."), "text/html; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
        }

        if (files.ReadFrame() is not { } html)
        {
            return Results.Content(Page("The widget is not built yet. Run npm run build:widget in web/."), "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        response.Headers.ContentSecurityPolicy = ContentSecurityPolicy(widget.Settings.Security.AllowedOrigins);
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    // Preview mode shows only the settings the editor posts and calls no API; only the portal may embed it.
    private static IResult Preview(HttpResponse response, WidgetAssetFiles files)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "no-cache";
        if (files.ReadFrame() is not { } html)
        {
            return Results.Content(Page("The widget is not built yet. Run npm run build:widget in web/."), "text/html; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        response.Headers.ContentSecurityPolicy = PreviewContentSecurityPolicy;
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> MediaAsync(Guid id, HttpResponse response, IWidgetRepository widgets, CancellationToken cancellationToken)
    {
        if (await widgets.GetAssetAsync(id, cancellationToken) is not { } asset)
        {
            return Results.NotFound();
        }

        // An image never changes under its id: a new upload gets a new one.
        response.Headers.CacheControl = "public, max-age=31536000, immutable";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return Results.Bytes(asset.Content, asset.ContentType);
    }

    private static IResult Static(string path, HttpResponse response, WidgetAssetFiles files)
    {
        if (files.Find(path) is not { } file)
        {
            return Results.NotFound();
        }

        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";

        // Built assets have content hashes in their names; the loader keeps its name, so host pages pick up new versions.
        response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "public, max-age=300";
        return Results.File(file.PhysicalPath, file.ContentType, lastModified: file.LastModified);
    }

    internal static readonly string PreviewContentSecurityPolicy = Policy("'self'");

    // No allowed origins means any site may embed the widget. The portal shows the preview, never this frame.
    internal static string ContentSecurityPolicy(IReadOnlyList<string> allowedOrigins) =>
        Policy(allowedOrigins.Count == 0 ? "*" : string.Join(' ', allowedOrigins));

    private static string Policy(string frameAncestors) =>
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
        + "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'; "
        + $"frame-ancestors {frameAncestors}";

    private static string Page(string message) =>
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Chat</title></head>"
        + $"<body style=\"font-family:system-ui,sans-serif;padding:1rem;color:#334155\"><p>{System.Net.WebUtility.HtmlEncode(message)}</p></body></html>";
}

internal sealed record WidgetFile(string PhysicalPath, string ContentType, DateTimeOffset LastModified);

// frame.html is cached until the file changes, so a watch build shows up without a restart.
internal sealed partial class WidgetAssetFiles
{
    private const string FrameFile = "frame.html";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly string _root;
    private readonly ILogger<WidgetAssetFiles> _logger;
    private readonly Lock _gate = new();
    private (DateTime Written, string Html)? _frame;
    private bool _warned;

    public WidgetAssetFiles(IOptions<WidgetOptions> options, IHostEnvironment environment, ILogger<WidgetAssetFiles> logger)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.AssetsPath));
        _logger = logger;
    }

    public string? ReadFrame()
    {
        var path = Path.Combine(_root, FrameFile);
        if (!File.Exists(path))
        {
            lock (_gate)
            {
                if (!_warned)
                {
                    _warned = true;
                    LogNotBuilt(_logger, _root);
                }
            }

            return null;
        }

        var written = File.GetLastWriteTimeUtc(path);
        lock (_gate)
        {
            if (_frame is { } cached && cached.Written == written)
            {
                return cached.Html;
            }
        }

        var html = File.ReadAllText(path);
        lock (_gate)
        {
            _frame = (written, html);
        }

        return html;
    }

    // Only the loader and the built assets; the frame goes through FrameAsync for its security headers.
    public WidgetFile? Find(string path)
    {
        if (string.IsNullOrEmpty(path) || (path != "loader.js" && !path.StartsWith("assets/", StringComparison.Ordinal)) || !Directory.Exists(_root))
        {
            return null;
        }

        using var provider = new PhysicalFileProvider(_root);
        var file = provider.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory || file.PhysicalPath is null || !ContentTypes.TryGetContentType(path, out var contentType))
        {
            return null;
        }

        return new WidgetFile(file.PhysicalPath, contentType, file.LastModified);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The widget build was not found in {Path}; run npm run build:widget in web/ or set Widgets:AssetsPath")]
    private static partial void LogNotBuilt(ILogger logger, string path);
}
