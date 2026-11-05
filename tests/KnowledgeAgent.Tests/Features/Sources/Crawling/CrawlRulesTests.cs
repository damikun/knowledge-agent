using KnowledgeAgent.Features.Sources.Crawling;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class CrawlRulesTests
{
    private static CrawlRules Rules(string start, CrawlScope scope, params string[] exclude) =>
        new(new Uri(start), new WebsiteCrawlSettings { Scope = scope, Exclude = exclude }, RobotsRules.AllowAll);

    [Theory]
    [InlineData("https://example.com/docs/guide", true)]
    [InlineData("https://example.com/docs/guide/install", true)]
    [InlineData("http://www.example.com/docs/guide/install", true)]
    [InlineData("https://example.com/docs/guides", false)]
    [InlineData("https://example.com/docs", false)]
    [InlineData("https://other.example.com/docs/guide/install", false)]
    public void Section_scope_keeps_the_page_and_the_pages_under_it(string url, bool inScope) =>
        Rules("https://example.com/docs/guide", CrawlScope.Section).InScope(new Uri(url)).ShouldBe(inScope);

    [Fact]
    public void A_start_page_with_a_file_name_covers_its_directory()
    {
        CrawlRules.SectionPrefix(new Uri("https://example.com/docs/index.html")).ShouldBe("/docs/");
        Rules("https://example.com/docs/index.html", CrawlScope.Section).InScope(new Uri("https://example.com/docs/setup")).ShouldBeTrue();
    }

    [Fact]
    public void Site_scope_keeps_the_whole_host_only()
    {
        var rules = Rules("https://example.com/docs/guide", CrawlScope.Site);

        rules.InScope(new Uri("https://example.com/pricing")).ShouldBeTrue();
        rules.InScope(new Uri("https://blog.example.com/")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/blog/*", "https://example.com/blog/2026/post", true)]
    [InlineData("/blog/*", "https://example.com/docs/blog/post", false)]
    [InlineData("/changelog", "https://example.com/changelog/v2", true)]
    [InlineData("tags", "https://example.com/docs/tags/api", true)]
    [InlineData("*.pdf", "https://example.com/files/report.PDF", true)]
    [InlineData("?print=1", "https://example.com/a?print=1", true)]
    public void Exclude_patterns_match_the_path(string pattern, string url, bool excluded)
    {
        var rules = Rules("https://example.com/", CrawlScope.Site, pattern);

        rules.IsExcluded(new Uri(url)).ShouldBe(excluded);
        rules.Allows(new Uri(url)).ShouldBe(!excluded);
    }

    [Fact]
    public void Robots_rules_are_applied()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /internal", "KnowledgeAgent").Rules;
        var rules = new CrawlRules(new Uri("https://example.com/"), new WebsiteCrawlSettings { Scope = CrawlScope.Site }, robots);

        rules.Allows(new Uri("https://example.com/internal/x")).ShouldBeFalse();
        rules.Allows(new Uri("https://example.com/public")).ShouldBeTrue();
    }
}
