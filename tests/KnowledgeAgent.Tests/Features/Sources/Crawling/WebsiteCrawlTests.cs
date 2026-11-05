using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.AspNetCore.Http;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class WebsiteCrawlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScriptedHttpServer Html(ScriptedHttpServer server, string path, string html) =>
        server.MapText(path, html, "text/html; charset=utf-8");

    [Fact]
    public async Task Links_are_followed_breadth_first_inside_the_section_to_depth_three()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs home", "/docs/a", "/docs/b#install", "/docs/b?utm_source=nav", "/blog/news", "mailto:help@example.com"));
        Html(server, "docs/a", SitePages.Article("Page A", "/docs/a/1"));
        Html(server, "docs/b", SitePages.Article("Page B", "/docs"));
        Html(server, "docs/a/1", SitePages.Article("Page A1", "/docs/a/1/2"));
        Html(server, "docs/a/1/2", SitePages.Article("Page A12", "/docs/a/1/2/3"));
        Html(server, "docs/a/1/2/3", SitePages.Article("Too deep"));
        Html(server, "blog/news", SitePages.Article("Blog"));
        await using var harness = new CrawlHarness();
        var folder = Guid.NewGuid();
        var website = await harness.AddWebsiteAsync(server.Url("docs"), folderId: folder);

        await harness.CrawlAsync(website.Id);

        var pages = await harness.PagesAsync(website.Id);
        pages.Select(page => new Uri(page.Url!).AbsolutePath).ShouldBe(["/docs", "/docs/a", "/docs/a/1", "/docs/a/1/2", "/docs/b"]);
        pages.ShouldAllBe(page => page.Status == SourceStatus.Ready && page.Type == SourceType.Url && page.FolderId == folder && page.LastFetchedAt != null);
        server.Hits("docs/a/1/2/3").ShouldBe(0);
        server.Hits("blog/news").ShouldBe(0);
        server.Hits("docs/b").ShouldBe(1);
        pages.Single(page => page.Url!.EndsWith("/docs/a", StringComparison.Ordinal)).Title.ShouldBe("Page A");

        // Citations point at the page the text came from.
        foreach (var page in pages)
        {
            harness.Context.VectorStore.ForSource(page.Id).ShouldNotBeEmpty();
            harness.Context.VectorStore.ForSource(page.Id).ShouldAllBe(chunk => chunk.Metadata[ChunkMetadataKeys.Url] == page.Url);
        }

        var stored = harness.Website(website.Id);
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.ShouldSatisfyAllConditions(
            () => stored.PagesFound.ShouldBe(5),
            () => stored.PagesDone.ShouldBe(5),
            () => stored.PagesFailed.ShouldBe(0),
            () => stored.PageCount.ShouldBe(5),
            () => stored.ChunkCount.ShouldBe(0));
        harness.Context.VectorStore.ForSource(website.Id).ShouldBeEmpty();
        server.Requests.ShouldAllBe(request => request.UserAgent!.StartsWith("KnowledgeAgent/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_page_is_its_own_step_between_discovery_and_finishing()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/a"));
        Html(server, "docs/a", SitePages.Article("A"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        harness.StepIds.ShouldBe(["discover", $"page {server.Url("docs").AbsoluteUri}", $"page {server.Url("docs/a").AbsoluteUri}", "finish"]);
    }

    [Fact]
    public async Task A_retried_run_skips_the_pages_that_are_done()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/a"));
        Html(server, "docs/a", SitePages.Article("A"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));
        var steps = new KnowledgeAgent.Infrastructure.Jobs.InProcessJobSteps(2, harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ct);

        await harness.CrawlAsync(website.Id, "run-1", steps);
        await harness.CrawlAsync(website.Id, "run-1", steps);

        server.Hits("docs").ShouldBe(1);
        server.Hits("docs/a").ShouldBe(1);
    }

    [Fact]
    public async Task Max_pages_and_exclude_patterns_limit_the_crawl()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/changelog/v1", "/docs/a", "/docs/b", "/docs/c"));
        foreach (var path in new[] { "docs/a", "docs/b", "docs/c", "docs/changelog/v1" })
        {
            Html(server, path, SitePages.Article(path));
        }

        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"), maxPages: 3, exclude: "/docs/changelog/*");

        await harness.CrawlAsync(website.Id);

        (await harness.PagesAsync(website.Id)).Select(page => new Uri(page.Url!).AbsolutePath).ShouldBe(["/docs", "/docs/a", "/docs/b"]);
        server.Hits("docs/changelog/v1").ShouldBe(0);
        server.Hits("docs/c").ShouldBe(0);
    }

    [Fact]
    public async Task Max_pages_is_capped_by_the_configured_limit()
    {
        await using var harness = new CrawlHarness(options => options.MaxCrawlPages = 50);

        var error = await Should.ThrowAsync<KnowledgeAgent.Infrastructure.Web.ValidationFailedException>(
            () => harness.AddWebsiteAsync(new Uri("https://example.com/docs"), maxPages: 51));

        error.Errors.ShouldContainKey("maxPages");
    }

    [Fact]
    public async Task Robots_txt_is_respected_and_requests_are_spaced_out()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapText("robots.txt", "User-agent: *\nDisallow: /\n\nUser-agent: KnowledgeAgent\nDisallow: /docs/private\nCrawl-delay: 2\n");
        Html(server, "docs", SitePages.Article("Docs", "/docs/private/keys", "/docs/public"));
        Html(server, "docs/public", SitePages.Article("Public"));
        Html(server, "docs/private/keys", SitePages.Article("Private"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        server.Hits("docs/private/keys").ShouldBe(0);
        (await harness.PagesAsync(website.Id)).Count.ShouldBe(2);

        // robots.txt and sitemap.xml at one second, then the pages at the two-second crawl delay.
        var requests = server.Requests.Count;
        requests.ShouldBe(4);
        harness.Clock.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1 + 2 + 2));
    }

    [Fact]
    public async Task A_start_page_that_robots_txt_disallows_fails_the_website()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapText("robots.txt", "User-agent: *\nDisallow: /docs\n");
        Html(server, "docs", SitePages.Article("Docs"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        harness.Website(website.Id).Status.ShouldBe(SourceStatus.Failed);
        harness.Website(website.Id).Error!.ShouldContain("robots.txt");
        server.Hits("docs").ShouldBe(0);
    }

    [Fact]
    public async Task The_sitemap_is_used_before_links_including_indexes_from_robots_txt()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapText("robots.txt", $"Sitemap: {server.Url("sitemap_index.xml")}\n");
        server.MapText("sitemap_index.xml", $"<sitemapindex><sitemap><loc>{server.Url("sitemap-docs.xml")}</loc></sitemap></sitemapindex>", "application/xml");
        server.MapText(
            "sitemap-docs.xml",
            $"""
            <urlset>
              <url><loc>{server.Url("docs/x")}</loc><lastmod>2026-10-01</lastmod></url>
              <url><loc>{server.Url("docs/y")}</loc></url>
              <url><loc>{server.Url("pricing")}</loc></url>
            </urlset>
            """,
            "application/xml");
        Html(server, "docs", SitePages.Article("Docs", "/docs/linked-only"));
        Html(server, "docs/x", SitePages.Article("X"));
        Html(server, "docs/y", SitePages.Article("Y"));
        Html(server, "docs/linked-only", SitePages.Article("Linked"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        (await harness.PagesAsync(website.Id)).Select(page => new Uri(page.Url!).AbsolutePath).ShouldBe(["/docs", "/docs/x", "/docs/y"]);
        server.Hits("docs/linked-only").ShouldBe(0);
        server.Hits("pricing").ShouldBe(0);
        (await harness.PagesAsync(website.Id)).Single(page => page.Url!.EndsWith("/docs/x", StringComparison.Ordinal))
            .Metadata[SourceMetadataKeys.SitemapLastModified].ShouldStartWith("2026-10-01T00:00:00");
    }

    [Fact]
    public async Task A_refresh_skips_unchanged_pages_updates_changed_ones_adds_new_ones_and_removes_missing_ones()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        var home = SitePages.Article("Docs", "/docs/etag", "/docs/same", "/docs/changes", "/docs/gone", "/docs/unlinked");
        Html(server, "docs", home);
        server.Map("docs/etag", async context =>
        {
            if (context.Request.Headers.IfNoneMatch == "\"v1\"")
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.Headers.ETag = "\"v1\"";
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(SitePages.Article("Etag page"), Ct);
        });
        Html(server, "docs/same", SitePages.Article("Same"));
        Html(server, "docs/changes", SitePages.Article("Before"));
        Html(server, "docs/gone", SitePages.Article("Gone"));
        Html(server, "docs/unlinked", SitePages.Article("Unlinked"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));
        await harness.CrawlAsync(website.Id, "run-1");
        var first = (await harness.PagesAsync(website.Id)).ToDictionary(page => new Uri(page.Url!).AbsolutePath);
        first.Count.ShouldBe(6);
        var embedded = harness.Context.Embeddings.Calls.Count;

        Html(server, "docs", SitePages.Article("Docs home", "/docs/etag", "/docs/same", "/docs/changes", "/docs/gone", "/docs/new"));
        Html(server, "docs/changes", SitePages.Article("After"));
        server.Map("docs/gone", context =>
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            return Task.CompletedTask;
        });
        Html(server, "docs/new", SitePages.Article("New"));
        harness.Context.Time.Advance(TimeSpan.FromDays(1));
        await harness.CrawlAsync(website.Id, "run-2");

        var second = (await harness.PagesAsync(website.Id)).ToDictionary(page => new Uri(page.Url!).AbsolutePath);
        second.Keys.Order(StringComparer.Ordinal).ShouldBe(["/docs", "/docs/changes", "/docs/etag", "/docs/new", "/docs/same"]);
        second["/docs/etag"].Id.ShouldBe(first["/docs/etag"].Id);
        second["/docs/etag"].ProcessedAt.ShouldBe(first["/docs/etag"].ProcessedAt);
        second["/docs/etag"].LastFetchedAt!.Value.ShouldBeGreaterThan(first["/docs/etag"].LastFetchedAt!.Value);
        second["/docs/same"].ProcessedAt.ShouldBe(first["/docs/same"].ProcessedAt);
        second["/docs/changes"].Title.ShouldBe("After");
        second["/docs/changes"].ProcessedAt.ShouldNotBe(first["/docs/changes"].ProcessedAt);
        harness.Context.VectorStore.ForSource(first["/docs/gone"].Id).ShouldBeEmpty();
        harness.Context.VectorStore.ForSource(first["/docs/unlinked"].Id).ShouldBeEmpty();

        // Only the home page (its links changed), the changed page and the new page were embedded again.
        (harness.Context.Embeddings.Calls.Count - embedded).ShouldBe(3);
        harness.Website(website.Id).Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task A_page_whose_sitemap_lastmod_is_unchanged_is_not_fetched_again()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        server.MapText("sitemap.xml", $"<urlset><url><loc>{server.Url("docs/x")}</loc><lastmod>2026-10-01</lastmod></url></urlset>", "application/xml");
        Html(server, "docs", SitePages.Article("Docs"));
        Html(server, "docs/x", SitePages.Article("X"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id, "run-1");
        await harness.CrawlAsync(website.Id, "run-2");

        server.Hits("docs/x").ShouldBe(1);
        server.Hits("docs").ShouldBe(2);
        (await harness.PagesAsync(website.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_javascript_app_shell_needs_a_browser_instead_of_becoming_empty_knowledge()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "app", """
            <html><head><title>Dashboard</title><script src="/bundle.js"></script></head>
            <body><div id="root"></div><noscript>You need to enable JavaScript to run this app.</noscript></body></html>
            """);
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("app"));

        await harness.CrawlAsync(website.Id);

        var page = (await harness.PagesAsync(website.Id)).Single();
        page.Status.ShouldBe(SourceStatus.NeedsBrowser);
        page.Error.ShouldBe(WebsiteCrawler.NeedsBrowserMessage);
        page.ChunkCount.ShouldBe(0);
        harness.Context.VectorStore.ForSource(page.Id).ShouldBeEmpty();
        harness.Website(website.Id).Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task A_page_that_keeps_failing_fails_alone_after_its_retries()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/flaky", "/docs/forbidden", "/docs/ok"));
        server.Map("docs/flaky", context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        });
        server.Map("docs/forbidden", context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        });
        Html(server, "docs/ok", SitePages.Article("OK"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id, maxAttempts: 3);

        server.Hits("docs/flaky").ShouldBe(3);
        server.Hits("docs/forbidden").ShouldBe(1);
        var pages = (await harness.PagesAsync(website.Id)).ToDictionary(page => new Uri(page.Url!).AbsolutePath);
        pages["/docs/flaky"].Status.ShouldBe(SourceStatus.Failed);
        pages["/docs/flaky"].Error!.ShouldContain("503");
        pages["/docs/forbidden"].Status.ShouldBe(SourceStatus.Failed);
        pages["/docs/ok"].Status.ShouldBe(SourceStatus.Ready);
        var stored = harness.Website(website.Id);
        stored.Status.ShouldBe(SourceStatus.Ready);
        stored.PagesFailed.ShouldBe(2);
        stored.PagesDone.ShouldBe(2);
    }

    [Fact]
    public async Task A_site_that_is_down_keeps_its_pages()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/a"));
        Html(server, "docs/a", SitePages.Article("A"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));
        await harness.CrawlAsync(website.Id, "run-1");

        server.Map("robots.txt", context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        });
        await harness.CrawlAsync(website.Id, "run-2");

        harness.Website(website.Id).Status.ShouldBe(SourceStatus.Failed);
        (await harness.PagesAsync(website.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Canonical_links_merge_duplicate_addresses_into_one_page()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs", "/docs/print-view", "/docs/guide"));
        Html(server, "docs/print-view", SitePages.Article("Guide").Replace("<head>", "<head><link rel=\"canonical\" href=\"/docs/guide\">", StringComparison.Ordinal));
        Html(server, "docs/guide", SitePages.Article("Guide"));
        await using var harness = new CrawlHarness();
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        (await harness.PagesAsync(website.Id)).Select(page => new Uri(page.Url!).AbsolutePath).ShouldBe(["/docs", "/docs/guide"]);
    }

    [Fact]
    public async Task Every_request_goes_through_the_network_address_policy()
    {
        await using var server = await ScriptedHttpServer.StartAsync();
        Html(server, "docs", SitePages.Article("Docs"));
        await using var harness = new CrawlHarness(options => options.AllowPrivateNetworks = false);
        var website = await harness.AddWebsiteAsync(server.Url("docs"));

        await harness.CrawlAsync(website.Id);

        server.Requests.ShouldBeEmpty();
        harness.Website(website.Id).Status.ShouldBe(SourceStatus.Failed);
        harness.Website(website.Id).Error!.ShouldContain("not allowed");
        (await harness.PagesAsync(website.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Crawled_links_and_redirects_to_private_addresses_are_never_requested()
    {
        var web = new FakeWebHandler()
            .Map("https://docs.example.com/robots.txt", _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("Sitemap: http://10.0.0.7/sitemap.xml\n"),
            })
            .Html("https://docs.example.com/docs", SitePages.Article("Docs", "/docs/go", "/docs/ok", "http://169.254.169.254/latest/meta-data"))
            .Redirect("https://docs.example.com/docs/go", "http://169.254.169.254/latest/meta-data")
            .Html("https://docs.example.com/docs/ok", SitePages.Article("OK"));
        await using var harness = new CrawlHarness(options => options.AllowPrivateNetworks = false, web);
        var website = await harness.AddWebsiteAsync(new Uri("https://docs.example.com/docs"), CrawlScope.Site);

        await harness.CrawlAsync(website.Id);

        web.WasRequested("169.254.169.254").ShouldBeFalse();
        web.WasRequested("10.0.0.7").ShouldBeFalse();
        var pages = (await harness.PagesAsync(website.Id)).ToDictionary(page => new Uri(page.Url!).AbsolutePath);
        pages["/docs/go"].Status.ShouldBe(SourceStatus.Failed);
        pages["/docs/go"].Error!.ShouldContain("not allowed");
        pages["/docs/ok"].Status.ShouldBe(SourceStatus.Ready);
    }
}
