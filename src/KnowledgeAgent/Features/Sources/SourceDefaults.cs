using KnowledgeAgent.Features.Sources.Extraction;

namespace KnowledgeAgent.Features.Sources;

internal static class SourceMetadataKeys
{
    public const string FinalUrl = "finalUrl";
    public const string SiteName = "siteName";
    public const string AutoTitle = "autoTitle";

    // Crawled pages: the validators for conditional requests, the sitemap's lastmod and the crawl that last saw the page.
    public const string ETag = "etag";
    public const string LastModified = "lastModified";
    public const string SitemapLastModified = "sitemapLastModified";
    public const string CrawlRun = "crawlRun";
}

internal static class SourceDefaults
{
    public const int MaxTitleLength = 300;
    public const int MaxUrlLength = 2048;
    public const int MaxSiteLength = 255;
    public const string UntitledDocument = "Untitled document";

    public static string TitleForUrl(Uri uri)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        return TextUtilities.Truncate(uri.IdnHost + path, MaxTitleLength);
    }

    public static string TitleForFile(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        return name.Length > 0 ? TextUtilities.Truncate(name, MaxTitleLength) : UntitledDocument;
    }

    public static string SiteOf(Uri uri) => NormalizeSite(uri.IdnHost);

    // "www.example.com" and "example.com" are one site, so pages added either way are counted and filtered together.
    public static string NormalizeSite(string host)
    {
        var site = host.Trim().TrimEnd('.').ToLowerInvariant();
        return site.StartsWith("www.", StringComparison.Ordinal) && site.Length > 4 ? site[4..] : site;
    }

    public static bool HasAutomaticTitle(Source source)
    {
        if (source.Metadata.TryGetValue(SourceMetadataKeys.AutoTitle, out var flag))
        {
            return bool.TryParse(flag, out var automatic) && automatic;
        }

        if (string.IsNullOrWhiteSpace(source.Title))
        {
            return true;
        }

        if (source.Type is SourceType.Url or SourceType.Website && Uri.TryCreate(source.Url, UriKind.Absolute, out var uri))
        {
            return string.Equals(source.Title, TitleForUrl(uri), StringComparison.Ordinal);
        }

        return source.FileName is { Length: > 0 } fileName
            && string.Equals(source.Title, TitleForFile(fileName), StringComparison.Ordinal);
    }

    public static string? CleanTitle(string? title)
    {
        var cleaned = TextNormalizer.Normalize(title).Replace('\n', ' ').Trim();
        return cleaned.Length == 0 ? null : TextUtilities.Truncate(cleaned, MaxTitleLength);
    }

    public static string TypeName(SourceType type) => type switch
    {
        SourceType.Url => "url",
        SourceType.Pdf => "pdf",
        SourceType.Text => "text",
        SourceType.Markdown => "markdown",
        SourceType.Website => "website",
        _ => type.ToString().ToLowerInvariant(),
    };
}
