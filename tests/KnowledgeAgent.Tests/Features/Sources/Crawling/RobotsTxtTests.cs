using KnowledgeAgent.Features.Sources.Crawling;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class RobotsTxtTests
{
    private const string Token = "KnowledgeAgent";

    private static bool Allowed(RobotsTxt robots, string path) => robots.Rules.IsAllowed(new Uri("https://example.com" + path));

    [Fact]
    public void The_group_for_the_agent_wins_over_the_wildcard_group()
    {
        var robots = RobotsTxt.Parse(
            """
            User-agent: *
            Disallow: /

            User-agent: knowledgeagent/2.0
            Disallow: /private
            Crawl-delay: 3
            """,
            Token);

        Allowed(robots, "/docs").ShouldBeTrue();
        Allowed(robots, "/private/x").ShouldBeFalse();
        robots.Rules.CrawlDelaySeconds.ShouldBe(3);
    }

    [Fact]
    public void Without_a_group_for_the_agent_the_wildcard_group_applies()
    {
        var robots = RobotsTxt.Parse("User-agent: OtherBot\nDisallow: /\n\nUser-agent: *\nDisallow: /admin # staff only\n", Token);

        Allowed(robots, "/admin/users").ShouldBeFalse();
        Allowed(robots, "/docs").ShouldBeTrue();
    }

    [Fact]
    public void The_longest_match_wins_and_allow_wins_a_tie()
    {
        var robots = RobotsTxt.Parse(
            """
            User-agent: *
            Disallow: /docs/
            Allow: /docs/public/
            Disallow: /tie
            Allow: /tie
            """,
            Token);

        Allowed(robots, "/docs/internal").ShouldBeFalse();
        Allowed(robots, "/docs/public/page").ShouldBeTrue();
        Allowed(robots, "/tie").ShouldBeTrue();
    }

    [Theory]
    [InlineData("/*.pdf$", "/files/report.pdf", true)]
    [InlineData("/*.pdf$", "/files/report.pdf?x=1", false)]
    [InlineData("/search*q=", "/search?lang=en&q=x", true)]
    [InlineData("/a$", "/ab", false)]
    [InlineData("/a", "/ab", true)]
    [InlineData("*", "/anything", true)]
    public void Patterns_support_wildcards_and_end_anchors(string pattern, string path, bool matches) =>
        RobotsRules.Matches(pattern, path).ShouldBe(matches);

    [Fact]
    public void Sitemap_lines_are_collected_and_empty_disallow_allows_everything()
    {
        var robots = RobotsTxt.Parse("Sitemap: https://example.com/sitemap_index.xml\nUser-agent: *\nDisallow:\n", Token);

        robots.Sitemaps.ShouldBe(["https://example.com/sitemap_index.xml"]);
        Allowed(robots, "/anything").ShouldBeTrue();
    }

    [Fact]
    public void Robots_txt_itself_is_always_allowed() =>
        Allowed(RobotsTxt.Parse("User-agent: *\nDisallow: /", Token), "/robots.txt").ShouldBeTrue();

    [Fact]
    public void The_product_token_is_taken_from_the_user_agent() =>
        RobotsTxt.ProductToken("KnowledgeAgent/1.0 (+https://github.com/knowledge-agent)").ShouldBe("KnowledgeAgent");
}
