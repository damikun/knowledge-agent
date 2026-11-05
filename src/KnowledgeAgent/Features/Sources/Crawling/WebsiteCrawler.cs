using System.Globalization;
using System.Text;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources.Crawling;

internal enum CrawlDiscovery
{
    Sitemap,
    Links,
}

internal sealed record PlannedPage(string Url, DateTimeOffset? LastModified, int Depth);

// What the discovery step hands the rest of the crawl. Stored as a step result, so it is plain data.
internal sealed record CrawlPlan(
    bool Found,
    string? Error,
    CrawlDiscovery Discovery,
    string StartUrl,
    WebsiteCrawlSettings Settings,
    RobotsRules Robots,
    IReadOnlyList<PlannedPage> Pages,
    int MaxPages)
{
    public static CrawlPlan Missing { get; } = new(false, null, CrawlDiscovery.Links, "", new WebsiteCrawlSettings(), RobotsRules.AllowAll, [], 0);

    public CrawlRules Rules() => new(new Uri(StartUrl), Settings, Robots);
}

internal enum PageOutcome
{
    Added,
    Updated,
    Unchanged,
    NeedsBrowser,
    Failed,
    Gone,
    Skipped,
    Duplicate,
}

internal sealed record PageResult(PageOutcome Outcome, Guid? PageId, IReadOnlyList<string> Links);

internal sealed record PageRequest(Guid WebsiteId, string RunId, CrawlPlan Plan, PlannedPage Page, bool CollectLinks, int Found);

internal sealed record FailedPage(string Url, string Error);

internal sealed partial class WebsiteCrawler(
    ISourceRepository repository,
    IVectorStore vectorStore,
    UrlFetcher fetcher,
    ContentExtractor extractor,
    IngestionPipeline pipeline,
    HostRateLimiter rateLimiter,
    IOptionsMonitor<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<WebsiteCrawler> logger)
{
    public const int MaxDepth = 3;
    public const int MaxLinksPerPage = 500;
    public const int MaxSitemapFetches = 10;
    public const int MaxRobotsBytes = 512 * 1024;

    // Less text than this on a page that runs scripts means the content is rendered in the browser.
    public const int MinPageTextLength = 100;

    public const string NeedsBrowserMessage =
        "The page shows almost no text until JavaScript runs in a browser, so there was nothing to index.";

    private static readonly TimeSpan MaxCrawlDelay = TimeSpan.FromSeconds(30);

    private string UserAgentToken => RobotsTxt.ProductToken(options.CurrentValue.UserAgent);

    public async Task<CrawlPlan> DiscoverAsync(Guid websiteId, string runId, CancellationToken cancellationToken)
    {
        var website = await repository.GetAsync(websiteId, cancellationToken).ConfigureAwait(false);
        if (website is not { Type: SourceType.Website } || CrawlUrl.Canonicalize(website.Url) is not { } start)
        {
            return CrawlPlan.Missing;
        }

        var settings = website.Crawl ?? new WebsiteCrawlSettings();
        var maxPages = Math.Clamp(settings.MaxPages, 1, options.CurrentValue.MaxCrawlPages);
        website.Status = SourceStatus.Processing;
        website.Stage = SourceStage.Fetching;
        website.Error = null;
        website.JobRunId = runId;
        website.Attempts++;
        website.PagesFound = 1;
        website.PagesDone = 0;
        website.PagesFailed = 0;
        await repository.UpdateAsync(website, cancellationToken).ConfigureAwait(false);

        RobotsTxt robots;
        try
        {
            robots = await ReadRobotsAsync(start, cancellationToken).ConfigureAwait(false);
        }
        catch (IngestionException ex) when (ex.IsPermanent)
        {
            return Plan(CrawlDiscovery.Links, RobotsRules.AllowAll, [], ErrorSanitizer.ToUserMessage(ex));
        }

        if (!robots.Rules.IsAllowed(start))
        {
            return Plan(CrawlDiscovery.Links, robots.Rules, [], "The site's robots.txt does not allow crawling this address.");
        }

        var rules = new CrawlRules(start, settings, robots.Rules);
        var pages = new List<PlannedPage> { new(CrawlUrl.Key(start), null, 0) };
        var fromSitemaps = await ReadSitemapsAsync(start, robots, rules, maxPages - 1, cancellationToken).ConfigureAwait(false);
        pages.AddRange(fromSitemaps.Where(page => page.Url != pages[0].Url).Take(maxPages - 1));

        var discovery = fromSitemaps.Count > 0 ? CrawlDiscovery.Sitemap : CrawlDiscovery.Links;
        await repository.UpdateCrawlProgressAsync(websiteId, pages.Count, 0, 0, cancellationToken).ConfigureAwait(false);
        LogDiscovered(logger, websiteId, pages.Count, discovery);
        return Plan(discovery, robots.Rules, pages, null);

        CrawlPlan Plan(CrawlDiscovery discovery, RobotsRules robotsRules, IReadOnlyList<PlannedPage> planned, string? error) =>
            new(true, error, discovery, CrawlUrl.Key(start), settings, robotsRules, planned, maxPages);
    }

    public async Task<PageResult> ProcessPageAsync(PageRequest request, CancellationToken cancellationToken)
    {
        var website = await repository.GetAsync(request.WebsiteId, cancellationToken).ConfigureAwait(false);
        if (website is null)
        {
            return new PageResult(PageOutcome.Skipped, null, []);
        }

        var uri = new Uri(request.Page.Url);
        var rules = request.Plan.Rules();
        var interval = Interval(request.Plan.Robots);
        var existing = await repository.FindPageAsync(website.Id, request.Page.Url, cancellationToken).ConfigureAwait(false);

        // The sitemap says the page has not changed since it was last ingested.
        if (existing is { Status: SourceStatus.Ready } && request.Page.LastModified is { } lastModified && !request.CollectLinks
            && existing.Metadata.TryGetValue(SourceMetadataKeys.SitemapLastModified, out var storedLastModified)
            && storedLastModified == FormatDate(lastModified))
        {
            await MarkSeenAsync(existing, request.RunId, fetched: false, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.Unchanged, existing.Id, [], cancellationToken).ConfigureAwait(false);
        }

        FetchedContent fetched;
        try
        {
            fetched = await fetcher.FetchAsync(uri, Polite(interval, Conditional(existing)), cancellationToken).ConfigureAwait(false);
        }
        catch (IngestionException ex) when (ex.StatusCode is 404 or 410)
        {
            if (existing is not null)
            {
                await RemovePageAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            }

            return await DoneAsync(request, PageOutcome.Gone, null, [], cancellationToken).ConfigureAwait(false);
        }
        catch (IngestionException ex) when (ex.IsPermanent)
        {
            var failed = await SaveFailureAsync(website, existing, uri, ErrorSanitizer.ToUserMessage(ex), request.RunId, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.Failed, failed.Id, [], cancellationToken).ConfigureAwait(false);
        }

        if (fetched.NotModified && existing is not null)
        {
            var storedLinks = request.CollectLinks ? await StoredLinksAsync(existing, rules, cancellationToken).ConfigureAwait(false) : [];
            existing.Metadata = Validators(existing.Metadata, fetched, request.Page);
            await MarkSeenAsync(existing, request.RunId, fetched: true, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.Unchanged, existing.Id, storedLinks, cancellationToken).ConfigureAwait(false);
        }

        var finalUri = CrawlUrl.Canonicalize(fetched.FinalUri) ?? uri;
        var isStart = CrawlUrl.Key(finalUri) == CrawlUrl.Key(rules.Start);
        var kind = ContentTypeDetector.Detect(fetched.Content, fetched.MediaType, fetched.FileName);
        if ((!isStart && !rules.Allows(finalUri)) || kind is not (ContentKind.Html or ContentKind.Pdf))
        {
            return await SkipAsync(request, existing, [], cancellationToken).ConfigureAwait(false);
        }

        var html = kind == ContentKind.Html ? HtmlLinks.Read(TextDecoding.DecodeHtml(fetched.Content, fetched.CharSet), finalUri, UserAgentToken) : HtmlLinks.None;
        var links = request.CollectLinks ? Follow(html.Links, rules, finalUri) : [];
        if (html.NoIndex)
        {
            return await SkipAsync(request, existing, links, cancellationToken).ConfigureAwait(false);
        }

        var pageUri = html.Canonical is { } canonical && (rules.Allows(canonical) || CrawlUrl.Key(canonical) == CrawlUrl.Key(rules.Start)) ? canonical : finalUri;
        var page = existing;
        if (CrawlUrl.Key(pageUri) != request.Page.Url)
        {
            var target = await repository.FindPageAsync(website.Id, CrawlUrl.Key(pageUri), cancellationToken).ConfigureAwait(false);
            if (target is not null && existing is not null && existing.Id != target.Id)
            {
                await RemovePageAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            }

            if (target is not null && SeenIn(target, request.RunId))
            {
                return await DoneAsync(request, PageOutcome.Duplicate, target.Id, links, cancellationToken).ConfigureAwait(false);
            }

            page = target ?? existing;
        }
        else if (existing is not null && SeenIn(existing, request.RunId))
        {
            return await DoneAsync(request, PageOutcome.Duplicate, existing.Id, links, cancellationToken).ConfigureAwait(false);
        }

        ExtractedDocument document;
        try
        {
            document = extractor.Extract(fetched.Content, new ContentDescriptor(fetched.MediaType, fetched.CharSet, fetched.FileName, finalUri), cancellationToken);
        }
        catch (IngestionException ex) when (ex.IsPermanent)
        {
            var failed = await SaveFailureAsync(website, page, pageUri, ErrorSanitizer.ToUserMessage(ex), request.RunId, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.Failed, failed.Id, links, cancellationToken).ConfigureAwait(false);
        }

        var isNew = page is null;
        page ??= NewPage(website, pageUri);
        var unchanged = !isNew && page.Status == SourceStatus.Ready && page.ContentHash == TextUtilities.Sha256Hex(document.Text);
        page.Url = CrawlUrl.Key(pageUri);
        page.Site = SourceDefaults.SiteOf(pageUri);
        page.ContentType = fetched.MediaType ?? page.ContentType;
        page.SizeBytes = fetched.Content.LongLength;
        page.LastFetchedAt = timeProvider.GetUtcNow();
        page.Metadata = Validators(page.Metadata, fetched, request.Page);
        page.Metadata[SourceMetadataKeys.FinalUrl] = fetched.FinalUri.AbsoluteUri;
        page.Metadata[SourceMetadataKeys.SiteName] = SourceDefaults.CleanTitle(document.SiteName) ?? fetched.FinalUri.IdnHost;
        if (SourceDefaults.HasAutomaticTitle(page) && SourceDefaults.CleanTitle(document.Title) is { } title)
        {
            page.Title = title;
        }

        if (isNew)
        {
            await repository.AddAsync(page, new SourceContent { SourceId = page.Id, RawBytes = fetched.Content }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await repository.SaveContentAsync(page.Id, extractedText: null, fetched.Content, cancellationToken).ConfigureAwait(false);
        }

        if (kind == ContentKind.Html && html.HasScripts && VisibleTextLength(document) < MinPageTextLength)
        {
            await vectorStore.DeleteBySourceAsync(page.Id, cancellationToken).ConfigureAwait(false);
            page.Status = SourceStatus.NeedsBrowser;
            page.Stage = null;
            page.Error = NeedsBrowserMessage;
            page.ChunkCount = 0;
            page.EmbeddedChunks = 0;
            page.ContentHash = null;
            await MarkSeenAsync(page, request.RunId, fetched: true, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.NeedsBrowser, page.Id, links, cancellationToken).ConfigureAwait(false);
        }

        if (unchanged)
        {
            await MarkSeenAsync(page, request.RunId, fetched: true, cancellationToken).ConfigureAwait(false);
            return await DoneAsync(request, PageOutcome.Unchanged, page.Id, links, cancellationToken).ConfigureAwait(false);
        }

        var status = await pipeline.IngestFetchedAsync(page, document, request.RunId, cancellationToken).ConfigureAwait(false);
        await MarkSeenAsync(page, request.RunId, fetched: true, cancellationToken).ConfigureAwait(false);
        var outcome = status == SourceStatus.Failed ? PageOutcome.Failed : isNew ? PageOutcome.Added : PageOutcome.Updated;
        return await DoneAsync(request, outcome, page.Id, links, cancellationToken).ConfigureAwait(false);
    }

    // Pages that failed every attempt are recorded, and pages this crawl did not reach are removed, unless the crawl
    // failed as a whole: a site that is down must not lose its pages.
    public async Task<bool> FinishAsync(Guid websiteId, string runId, string? error, IReadOnlyList<FailedPage> failedPages, CancellationToken cancellationToken)
    {
        var website = await repository.GetAsync(websiteId, cancellationToken).ConfigureAwait(false);
        if (website is null)
        {
            return false;
        }

        foreach (var failed in failedPages)
        {
            var uri = new Uri(failed.Url);
            var existing = await repository.FindPageAsync(websiteId, failed.Url, cancellationToken).ConfigureAwait(false);
            await SaveFailureAsync(website, existing, uri, ErrorSanitizer.Sanitize(failed.Error), runId, cancellationToken).ConfigureAwait(false);
        }

        var pages = await repository.ListPagesAsync(websiteId, cancellationToken).ConfigureAwait(false);
        var current = pages.Where(page => SeenIn(page, runId)).ToList();
        var succeeded = current.Count(page => page.Status != SourceStatus.Failed);
        var failedCount = current.Count - succeeded;
        if (error is null && succeeded > 0)
        {
            foreach (var stale in pages.Where(page => !SeenIn(page, runId)))
            {
                await RemovePageAsync(stale.Id, cancellationToken).ConfigureAwait(false);
            }

            pages = current;
        }

        var now = timeProvider.GetUtcNow();
        website.Status = error is null && succeeded > 0 ? SourceStatus.Ready : SourceStatus.Failed;
        website.Error = error is not null ? ErrorSanitizer.Sanitize(error) : (succeeded > 0 ? null : current.Select(page => page.Error).FirstOrDefault(message => message is not null) ?? "No pages could be crawled.");
        website.Stage = null;
        website.PageCount = pages.Count;
        website.ProcessedAt = now;
        website.UpdatedAt = now;
        if (error is null)
        {
            website.PagesFound = Math.Max(website.PagesFound, current.Count);
            website.PagesFailed = failedCount;
            website.PagesDone = Math.Max(0, website.PagesFound - failedCount);
        }

        await repository.UpdateAsync(website, cancellationToken).ConfigureAwait(false);
        LogFinished(logger, websiteId, pages.Count, failedCount, website.Status);
        return true;
    }

    private async Task<RobotsTxt> ReadRobotsAsync(Uri start, CancellationToken cancellationToken)
    {
        var robotsUri = new Uri(start, "/robots.txt");
        try
        {
            var fetched = await fetcher.FetchAsync(robotsUri, Polite(Interval(RobotsRules.AllowAll), new FetchOptions { Accept = "text/plain,*/*;q=0.5" }), cancellationToken)
                .ConfigureAwait(false);
            var length = Math.Min(fetched.Content.Length, MaxRobotsBytes);
            return RobotsTxt.Parse(Encoding.UTF8.GetString(fetched.Content, 0, length), UserAgentToken);
        }
        catch (IngestionException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            // RFC 9309: a robots.txt that is missing or forbidden places no restrictions.
            return RobotsTxt.Empty;
        }
    }

    private async Task<List<PlannedPage>> ReadSitemapsAsync(Uri start, RobotsTxt robots, CrawlRules rules, int limit, CancellationToken cancellationToken)
    {
        var pages = new List<PlannedPage>();
        if (limit <= 0)
        {
            return pages;
        }

        var site = SourceDefaults.SiteOf(start);
        var queue = new Queue<(Uri Uri, int Depth)>();
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in robots.Sitemaps.Select(url => CrawlUrl.Canonicalize(url)).Append(new Uri(start, "/sitemap.xml")))
        {
            if (candidate is not null && SourceDefaults.SiteOf(candidate) == site && queued.Add(candidate.AbsoluteUri))
            {
                queue.Enqueue((candidate, 0));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fetches = 0;
        var interval = Interval(robots.Rules);
        var maxBytes = options.CurrentValue.MaxDownloadBytes;
        while (queue.TryDequeue(out var next) && fetches++ < MaxSitemapFetches && pages.Count < limit)
        {
            Sitemap sitemap;
            try
            {
                var fetched = await fetcher.FetchAsync(next.Uri, Polite(interval, new FetchOptions { Accept = "application/xml,text/xml,*/*;q=0.5" }), cancellationToken)
                    .ConfigureAwait(false);
                sitemap = SitemapParser.Parse(fetched.Content, maxBytes);
            }
            catch (Exception ex) when (ex is IngestionException or InvalidDataException)
            {
                // No sitemap or a broken one: links are followed instead.
                LogSitemapSkipped(logger, next.Uri, ex.Message);
                continue;
            }

            foreach (var entry in sitemap.Pages)
            {
                if (pages.Count >= limit)
                {
                    break;
                }

                if (CrawlUrl.Canonicalize(entry.Url) is { } uri && rules.Allows(uri) && seen.Add(CrawlUrl.Key(uri)))
                {
                    pages.Add(new PlannedPage(CrawlUrl.Key(uri), entry.LastModified, 0));
                }
            }

            if (next.Depth < 2)
            {
                foreach (var child in sitemap.Sitemaps.Select(url => CrawlUrl.Canonicalize(url)))
                {
                    if (child is not null && SourceDefaults.SiteOf(child) == site && queued.Add(child.AbsoluteUri))
                    {
                        queue.Enqueue((child, next.Depth + 1));
                    }
                }
            }
        }

        return pages;
    }

    private async Task<IReadOnlyList<string>> StoredLinksAsync(Source page, CrawlRules rules, CancellationToken cancellationToken)
    {
        if (page.ContentType is not ("text/html" or "application/xhtml+xml")
            || await repository.GetContentAsync(page.Id, cancellationToken).ConfigureAwait(false) is not { RawBytes: { Length: > 0 } raw })
        {
            return [];
        }

        var baseUri = page.Metadata.TryGetValue(SourceMetadataKeys.FinalUrl, out var finalUrl) && Uri.TryCreate(finalUrl, UriKind.Absolute, out var final)
            ? final
            : new Uri(page.Url!);
        var html = HtmlLinks.Read(TextDecoding.DecodeHtml(raw, charSet: null), baseUri, UserAgentToken);
        return Follow(html.Links, rules, baseUri);
    }

    private static List<string> Follow(IReadOnlyList<Uri> links, CrawlRules rules, Uri from) =>
        [.. links.Where(link => CrawlUrl.Key(link) != CrawlUrl.Key(from) && rules.Allows(link)).Select(CrawlUrl.Key).Take(MaxLinksPerPage)];

    private async Task<PageResult> SkipAsync(PageRequest request, Source? existing, IReadOnlyList<string> links, CancellationToken cancellationToken)
    {
        if (existing is not null)
        {
            await RemovePageAsync(existing.Id, cancellationToken).ConfigureAwait(false);
        }

        return await DoneAsync(request, PageOutcome.Skipped, null, links, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PageResult> DoneAsync(PageRequest request, PageOutcome outcome, Guid? pageId, IReadOnlyList<string> links, CancellationToken cancellationToken)
    {
        var failed = outcome == PageOutcome.Failed ? 1 : 0;
        await repository.UpdateCrawlProgressAsync(request.WebsiteId, request.Found, 1 - failed, failed, cancellationToken).ConfigureAwait(false);
        return new PageResult(outcome, pageId, links);
    }

    private async Task<Source> SaveFailureAsync(Source website, Source? existing, Uri uri, string error, string runId, CancellationToken cancellationToken)
    {
        var page = existing ?? NewPage(website, uri);
        page.Status = SourceStatus.Failed;
        page.Stage = null;
        page.Error = error;
        page.LastFetchedAt = timeProvider.GetUtcNow();
        page.Metadata[SourceMetadataKeys.CrawlRun] = runId;
        if (existing is null)
        {
            await repository.AddAsync(page, content: null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await repository.UpdateAsync(page, cancellationToken).ConfigureAwait(false);
        }

        return page;
    }

    private async Task MarkSeenAsync(Source page, string runId, bool fetched, CancellationToken cancellationToken)
    {
        page.Metadata = new Dictionary<string, string>(page.Metadata) { [SourceMetadataKeys.CrawlRun] = runId };
        if (fetched)
        {
            page.LastFetchedAt = timeProvider.GetUtcNow();
        }

        await repository.UpdateAsync(page, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemovePageAsync(Guid pageId, CancellationToken cancellationToken)
    {
        await vectorStore.DeleteBySourceAsync(pageId, cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(pageId, cancellationToken).ConfigureAwait(false);
    }

    private Source NewPage(Source website, Uri uri)
    {
        var now = timeProvider.GetUtcNow();
        return new Source
        {
            Id = Guid.CreateVersion7(now),
            Type = SourceType.Url,
            Status = SourceStatus.Pending,
            Url = CrawlUrl.Key(uri),
            Site = SourceDefaults.SiteOf(uri),
            Title = SourceDefaults.TitleForUrl(uri),
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = new Dictionary<string, string> { [SourceMetadataKeys.AutoTitle] = "true" },
            FolderId = website.FolderId,
            ParentId = website.Id,
        };
    }

    private FetchOptions Polite(TimeSpan interval, FetchOptions? fetchOptions = null) =>
        (fetchOptions ?? FetchOptions.Default) with { BeforeRequest = (uri, token) => rateLimiter.WaitAsync(uri.Host, interval, token) };

    private TimeSpan Interval(RobotsRules robots)
    {
        var perRequest = TimeSpan.FromSeconds(1 / Math.Clamp(options.CurrentValue.CrawlRequestsPerSecond, 0.01, IngestionOptionsValidator.MaxCrawlRequestsPerSecond));
        var crawlDelay = robots.CrawlDelaySeconds is { } seconds ? TimeSpan.FromSeconds(Math.Min(seconds, MaxCrawlDelay.TotalSeconds)) : TimeSpan.Zero;
        return perRequest > crawlDelay ? perRequest : crawlDelay;
    }

    // Validators are sent only for a page that is ready, so a page that failed or waits is fetched in full.
    private static FetchOptions Conditional(Source? existing) =>
        existing is { Status: SourceStatus.Ready }
            ? new FetchOptions
            {
                IfNoneMatch = existing.Metadata.GetValueOrDefault(SourceMetadataKeys.ETag),
                IfModifiedSince = existing.Metadata.GetValueOrDefault(SourceMetadataKeys.LastModified),
            }
            : FetchOptions.Default;

    private static Dictionary<string, string> Validators(Dictionary<string, string> metadata, FetchedContent fetched, PlannedPage page)
    {
        var updated = new Dictionary<string, string>(metadata);
        Set(SourceMetadataKeys.ETag, fetched.ETag);
        Set(SourceMetadataKeys.LastModified, fetched.LastModified);
        Set(SourceMetadataKeys.SitemapLastModified, page.LastModified is { } lastModified ? FormatDate(lastModified) : null);
        return updated;

        void Set(string key, string? value)
        {
            if (value is null)
            {
                updated.Remove(key);
            }
            else
            {
                updated[key] = value;
            }
        }
    }

    private static bool SeenIn(Source page, string runId) =>
        page.Metadata.TryGetValue(SourceMetadataKeys.CrawlRun, out var seen) && seen == runId;

    private static int VisibleTextLength(ExtractedDocument document)
    {
        var text = document.Text.AsSpan().Trim();
        if (document.Title is { Length: > 0 } title && text.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            text = text[title.Length..].Trim();
        }

        return text.Length;
    }

    private static string FormatDate(DateTimeOffset date) => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Website {WebsiteId}: {Count} pages to start from ({Discovery})")]
    private static partial void LogDiscovered(ILogger logger, Guid websiteId, int count, CrawlDiscovery discovery);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped sitemap {Sitemap}: {Reason}")]
    private static partial void LogSitemapSkipped(ILogger logger, Uri sitemap, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Website {WebsiteId} crawled: {Pages} pages, {Failed} failed, status {Status}")]
    private static partial void LogFinished(ILogger logger, Guid websiteId, int pages, int failed, SourceStatus status);
}
