using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Features.Sources.Support;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

public sealed class WebsiteSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<Source> AddAsync(KnowledgeTestContext context, string url, WebsiteCrawlSettings settings, Guid? folderId = null) =>
        context.WithSourceServiceAsync(sources => sources.AddWebsiteAsync(url, settings, folderId: folderId, cancellationToken: Ct));

    [Fact]
    public async Task This_page_only_adds_a_plain_url_source()
    {
        await using var context = new KnowledgeTestContext();

        var source = await AddAsync(context, "https://example.com/docs/intro", new WebsiteCrawlSettings { Scope = CrawlScope.Page });

        source.Type.ShouldBe(SourceType.Url);
        source.Crawl.ShouldBeNull();
        context.DrainQueue().ShouldBe([source.Id]);
        context.Queue.Crawls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_website_is_stored_with_its_settings_and_its_crawl_is_queued()
    {
        await using var context = new KnowledgeTestContext();

        var website = await AddAsync(
            context,
            "https://Example.com/docs/?utm_source=x#top",
            new WebsiteCrawlSettings { Scope = CrawlScope.Site, MaxPages = 25, Exclude = [" /blog/* ", "", "/blog/*"] },
            SourceFolderIds.Internal);

        website.Type.ShouldBe(SourceType.Website);
        website.Url.ShouldBe("https://example.com/docs");
        website.Site.ShouldBe("example.com");
        website.FolderId.ShouldBe(SourceFolderIds.Internal);
        website.Crawl.ShouldNotBeNull().ShouldSatisfyAllConditions(
            crawl => crawl.Scope.ShouldBe(CrawlScope.Site),
            crawl => crawl.MaxPages.ShouldBe(25),
            crawl => crawl.Exclude.ShouldBe(["/blog/*"]));
        context.Queue.Crawls.ShouldBe([website.Id]);
        context.DrainQueue().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0, "maxPages")]
    [InlineData(501, 0, "maxPages")]
    [InlineData(10, 21, "exclude")]
    public async Task Invalid_crawl_settings_are_rejected(int maxPages, int excludeCount, string field)
    {
        await using var context = new KnowledgeTestContext();
        var exclude = Enumerable.Range(0, excludeCount).Select(i => $"/p{i}").ToArray();

        var error = await Should.ThrowAsync<ValidationFailedException>(() =>
            AddAsync(context, "https://example.com/", new WebsiteCrawlSettings { MaxPages = maxPages, Exclude = exclude }));

        error.Errors.ShouldContainKey(field);
    }

    [Fact]
    public async Task Deleting_a_website_deletes_its_pages_and_their_vectors()
    {
        await using var context = new KnowledgeTestContext();
        var website = await AddAsync(context, "https://example.com/docs", new WebsiteCrawlSettings());
        var page = await context.SeedAsync(new Source { Type = SourceType.Url, Title = "Page", Url = "https://example.com/docs/a", ParentId = website.Id });
        var other = await context.SeedTextAsync("Other", "Unrelated text.");
        await context.VectorStore.InsertAsync([Chunk(page.Id), Chunk(other.Id)], Ct);

        (await context.WithSourceServiceAsync(sources => sources.DeleteAsync(website.Id, Ct))).ShouldBeTrue();

        context.Repository.Find(website.Id).ShouldBeNull();
        context.Repository.Find(page.Id).ShouldBeNull();
        context.VectorStore.ForSource(page.Id).ShouldBeEmpty();
        context.VectorStore.ForSource(other.Id).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Re_ingesting_a_website_queues_its_crawl_and_the_pipeline_hands_websites_to_it()
    {
        await using var context = new KnowledgeTestContext();
        var website = await AddAsync(context, "https://example.com/docs", new WebsiteCrawlSettings());
        context.Queue.Crawls.Clear();

        await context.WithSourceServiceAsync(sources => sources.ReingestAsync(website.Id, Ct));
        await context.ProcessAsync(website.Id, Ct);

        context.Queue.Crawls.ShouldBe([website.Id, website.Id]);
        context.DrainQueue().ShouldBeEmpty();
        context.Repository.Find(website.Id)!.Status.ShouldBe(SourceStatus.Pending);
    }

    [Fact]
    public async Task Re_indexing_everything_leaves_crawled_pages_to_their_website()
    {
        await using var context = new KnowledgeTestContext();
        var website = await AddAsync(context, "https://example.com/docs", new WebsiteCrawlSettings());
        var page = await context.SeedAsync(new Source { Type = SourceType.Url, Status = SourceStatus.Ready, Title = "Page", Url = "https://example.com/docs/a", ParentId = website.Id });
        var single = await context.SeedTextAsync("Single", "Some text.");
        context.Queue.Crawls.Clear();

        await context.WithSourceServiceAsync(sources => sources.ReingestAllAsync(Ct));

        context.DrainQueue().ShouldBe([single.Id]);
        context.Queue.Crawls.ShouldBe([website.Id]);
        context.Repository.Find(page.Id)!.Status.ShouldBe(SourceStatus.Pending);
    }

    [Fact]
    public async Task The_library_lists_websites_without_their_pages()
    {
        await using var context = new KnowledgeTestContext();
        var website = await AddAsync(context, "https://example.com/docs", new WebsiteCrawlSettings());
        await context.SeedAsync(new Source { Type = SourceType.Url, Status = SourceStatus.Ready, ChunkCount = 4, Title = "Page", Url = "https://example.com/docs/a", ParentId = website.Id });

        var stats = await context.WithSourceServiceAsync(sources => sources.GetStatsAsync(Ct));
        stats.CountByStatus.Values.Sum().ShouldBe(1);
        stats.ReadyChunkCount.ShouldBe(4);

        var topLevel = await context.WithSourceServiceAsync(sources => sources.FindAsync(new SourceQuery(Parent: ParentFilter.TopLevel), Ct));
        var pages = await context.WithSourceServiceAsync(sources => sources.FindAsync(new SourceQuery(Parent: new ParentFilter(website.Id)), Ct));

        topLevel.Items.Select(source => source.Id).ShouldBe([website.Id]);
        pages.Total.ShouldBe(1);
    }

    private static ChunkRecord Chunk(Guid sourceId) =>
        new(Guid.NewGuid(), sourceId, 0, "text", "model", new float[] { 1, 0 }, new Dictionary<string, string>(), "");

    private static class SourceFolderIds
    {
        public static readonly Guid Internal = KnowledgeAgent.Features.Sources.Folders.SourceFolder.InternalId;
    }
}
