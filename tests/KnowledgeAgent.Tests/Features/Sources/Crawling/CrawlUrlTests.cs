using KnowledgeAgent.Features.Sources.Crawling;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class CrawlUrlTests
{
    [Theory]
    [InlineData("https://Docs.Example.com/Guide/#install", "https://docs.example.com/Guide")]
    [InlineData("https://docs.example.com/guide/", "https://docs.example.com/guide")]
    [InlineData("https://docs.example.com", "https://docs.example.com/")]
    [InlineData("https://docs.example.com:443/a", "https://docs.example.com/a")]
    [InlineData("https://docs.example.com/a?utm_source=x&page=2&gclid=1&fbclid=2&UTM_Medium=y", "https://docs.example.com/a?page=2")]
    [InlineData("https://docs.example.com/a?utm_campaign=x", "https://docs.example.com/a")]
    [InlineData("http://docs.example.com/a?b=1&c=2", "http://docs.example.com/a?b=1&c=2")]
    public void Urls_are_canonicalized(string url, string expected) =>
        CrawlUrl.Canonicalize(url)!.AbsoluteUri.ShouldBe(expected);

    [Theory]
    [InlineData("mailto:help@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("")]
    public void Urls_that_cannot_be_crawled_are_rejected(string url) => CrawlUrl.Canonicalize(url).ShouldBeNull();

    [Fact]
    public void Relative_links_resolve_against_the_page()
    {
        var page = new Uri("https://example.com/docs/guide/intro");

        CrawlUrl.Canonicalize("../api/#top", page)!.AbsoluteUri.ShouldBe("https://example.com/docs/api");
        CrawlUrl.Canonicalize("setup?utm_source=nav", page)!.AbsoluteUri.ShouldBe("https://example.com/docs/guide/setup");
    }
}
