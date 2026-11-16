namespace KnowledgeAgent.Features.Sources.Crawling;

public enum CrawlScope
{
    Page,

    Section,

    Site,
}

public sealed record WebsiteCrawlSettings
{
    public const int DefaultMaxPages = 100;
    public const int MaxExcludePatterns = 20;
    public const int MaxExcludePatternLength = 200;

    public CrawlScope Scope { get; init; } = CrawlScope.Section;
    public int MaxPages { get; init; } = DefaultMaxPages;
    public IReadOnlyList<string> Exclude { get; init; } = [];
}
