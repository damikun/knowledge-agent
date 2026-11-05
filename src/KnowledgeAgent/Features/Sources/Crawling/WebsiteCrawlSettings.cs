namespace KnowledgeAgent.Features.Sources.Crawling;

public enum CrawlScope
{
    // Only the URL itself: a plain Url source, never a website.
    Page,

    // The page and the pages under its path on the same host.
    Section,

    // Every page on the host.
    Site,
}

// Replaced as a whole when it changes, never edited in place.
public sealed record WebsiteCrawlSettings
{
    public const int DefaultMaxPages = 100;
    public const int MaxExcludePatterns = 20;
    public const int MaxExcludePatternLength = 200;

    public CrawlScope Scope { get; init; } = CrawlScope.Section;
    public int MaxPages { get; init; } = DefaultMaxPages;
    public IReadOnlyList<string> Exclude { get; init; } = [];
}
