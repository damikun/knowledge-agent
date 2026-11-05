using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using SmartReader;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal sealed partial class HtmlTextExtractor(ILogger<HtmlTextExtractor> logger)
{
    internal const int MinReadableLength = 200;

    // SmartReader recurses per nesting level and slows down sharply on deep documents. Real pages stay far below.
    internal const int MaxReadabilityDepth = 200;

    private const int MaxTitleLength = 300;
    private static readonly Uri PlaceholderUri = new("https://localhost/");

    public ExtractedDocument Extract(string html, Uri? uri)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var address = uri is { IsAbsoluteUri: true } ? uri : PlaceholderUri;

        var depth = HtmlToText.MaxDepth(document);
        var readable = depth <= MaxReadabilityDepth ? ExtractReadable(parser, html, address) : default;
        if (depth > MaxReadabilityDepth)
        {
            LogReadabilitySkipped(logger, address, depth);
        }

        var title = readable.Title ?? DocumentTitle(document);
        var siteName = readable.SiteName ?? CleanTitle(Meta(document, "og:site_name") ?? Meta(document, "application-name"));

        string text;
        if (readable.Text is { Length: >= MinReadableLength })
        {
            text = PrependTitle(readable.Text, readable.Title);
        }
        else
        {
            var fallback = TextNormalizer.Normalize(HtmlToText.Convert(document.Body ?? (INode?)document.DocumentElement ?? document));
            text = readable.Text is { } shortReadable && shortReadable.Length > fallback.Length
                ? PrependTitle(shortReadable, readable.Title)
                : fallback;
        }

        return new ExtractedDocument(text, title, PageCount: null, ContentKind.Html, siteName);
    }

    private (string? Text, string? Title, string? SiteName) ExtractReadable(HtmlParser parser, string html, Uri address)
    {
        try
        {
            // SmartReader mutates the document, so it gets its own copy. Given a parsed document,
            // it never makes network requests itself.
            using var document = parser.ParseDocument(html);
            using var reader = new Reader(address.AbsoluteUri, document);
            var article = reader.GetArticle();
            var title = CleanTitle(article.Title);
            var siteName = CleanTitle(article.SiteName);
            if (!article.IsReadable || string.IsNullOrWhiteSpace(article.Content))
            {
                return (null, title, siteName);
            }

            using var content = parser.ParseDocument(article.Content);
            var text = TextNormalizer.Normalize(HtmlToText.Convert(content.Body ?? (INode)content));
            return (text.Length > 0 ? text : null, title, siteName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReadabilityFailed(logger, ex, address);
            return (null, null, null);
        }
    }

    // Readability drops the heading that repeats the title, so the title is put back as the first line.
    private static string PrependTitle(string text, string? title) =>
        string.IsNullOrEmpty(title) || text.StartsWith(title, StringComparison.OrdinalIgnoreCase)
            ? text
            : title + "\n\n" + text;

    private static string? DocumentTitle(IHtmlDocument document)
    {
        var heading = document.QuerySelector("h1");
        return CleanTitle(Meta(document, "og:title"))
            ?? CleanTitle(document.Title)
            ?? (heading is null ? null : CleanTitle(HtmlToText.Convert(heading)));
    }

    private static string? Meta(IHtmlDocument document, string name)
    {
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var key = meta.GetAttribute("property") ?? meta.GetAttribute("name");
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return meta.GetAttribute("content");
            }
        }

        return null;
    }

    internal static string? CleanTitle(string? title)
    {
        var cleaned = TextNormalizer.Normalize(title).Replace('\n', ' ');
        if (cleaned.Length == 0)
        {
            return null;
        }

        return TextUtilities.Truncate(cleaned, MaxTitleLength);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Readability extraction failed for {Address}; using the full page text")]
    private static partial void LogReadabilityFailed(ILogger logger, Exception exception, Uri address);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped readability for {Address}: the document is nested {Depth} levels deep")]
    private static partial void LogReadabilitySkipped(ILogger logger, Uri address, int depth);
}
