using System.IO.Compression;
using System.Text;
using KnowledgeAgent.Features.Sources.Crawling;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class SitemapParserTests
{
    private const long MaxBytes = 1024 * 1024;

    [Fact]
    public void A_urlset_yields_its_pages_with_lastmod()
    {
        var sitemap = SitemapParser.Parse(
            Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc> https://example.com/a </loc><lastmod>2026-10-01</lastmod><priority>0.5</priority></url>
                  <url><lastmod>2026-10-02T08:30:00+02:00</lastmod><loc>https://example.com/b</loc></url>
                  <url><loc>https://example.com/c</loc></url>
                </urlset>
                """),
            MaxBytes);

        sitemap.Sitemaps.ShouldBeEmpty();
        sitemap.Pages.Select(page => page.Url).ShouldBe(["https://example.com/a", "https://example.com/b", "https://example.com/c"]);
        sitemap.Pages[0].LastModified.ShouldBe(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        sitemap.Pages[1].LastModified.ShouldBe(new DateTimeOffset(2026, 10, 2, 6, 30, 0, TimeSpan.Zero));
        sitemap.Pages[2].LastModified.ShouldBeNull();
    }

    [Fact]
    public void A_sitemap_index_yields_its_sitemaps()
    {
        var sitemap = SitemapParser.Parse(
            Encoding.UTF8.GetBytes(
                """
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>https://example.com/sitemap-docs.xml</loc><lastmod>2026-10-01</lastmod></sitemap>
                  <sitemap><loc>https://example.com/sitemap-blog.xml.gz</loc></sitemap>
                </sitemapindex>
                """),
            MaxBytes);

        sitemap.Pages.ShouldBeEmpty();
        sitemap.Sitemaps.ShouldBe(["https://example.com/sitemap-docs.xml", "https://example.com/sitemap-blog.xml.gz"]);
    }

    [Fact]
    public void Gzipped_and_plain_text_sitemaps_are_read()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("<urlset><url><loc>https://example.com/zipped</loc></url></urlset>"));
        }

        SitemapParser.Parse(compressed.ToArray(), MaxBytes).Pages.Single().Url.ShouldBe("https://example.com/zipped");
        SitemapParser.Parse(Encoding.UTF8.GetBytes("https://example.com/one\n# note\nhttps://example.com/two\n"), MaxBytes)
            .Pages.Select(page => page.Url).ShouldBe(["https://example.com/one", "https://example.com/two"]);
    }

    [Fact]
    public void Documents_with_a_dtd_are_refused()
    {
        var sitemap = SitemapParser.Parse(
            Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0"?>
                <!DOCTYPE urlset [<!ENTITY lol "lol">]>
                <urlset><url><loc>https://example.com/&lol;</loc></url></urlset>
                """),
            MaxBytes);

        sitemap.Pages.ShouldBeEmpty();
    }
}
