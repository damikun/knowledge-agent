using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Sources.Crawling;

// A knowledge test context with the crawler on a jumping clock, so rate limits and step retries cost no real time.
internal sealed class CrawlHarness : IAsyncDisposable
{
    public CrawlHarness(Action<IngestionOptions>? configure = null, FakeWebHandler? web = null)
    {
        Context = new KnowledgeTestContext(
            options =>
            {
                options.CrawlRequestsPerSecond = 1;
                configure?.Invoke(options);
            },
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton(new HostRateLimiter(Clock)));
                if (web is not null)
                {
                    services.AddHttpClient(UrlFetcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => web);
                }
            });
    }

    public KnowledgeTestContext Context { get; }

    public InstantTimeProvider Clock { get; } = new();

    public List<string> StepIds { get; } = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<Source> AddWebsiteAsync(Uri url, CrawlScope scope = CrawlScope.Section, int maxPages = 100, Guid? folderId = null, params string[] exclude) =>
        Context.WithSourceServiceAsync(sources => sources.AddWebsiteAsync(
            url.AbsoluteUri,
            new WebsiteCrawlSettings { Scope = scope, MaxPages = maxPages, Exclude = exclude },
            folderId: folderId,
            cancellationToken: Ct));

    public Task CrawlAsync(Guid websiteId, string runId = "run-1", int maxAttempts = 2) =>
        CrawlAsync(websiteId, runId, new InProcessJobSteps(maxAttempts, Clock, NullLogger.Instance, Ct));

    public Task CrawlAsync(Guid websiteId, string runId, IJobSteps steps) =>
        Context.WithScopeAsync(async services =>
        {
            var job = services.GetRequiredService<CrawlWebsiteJob>();
            var context = new JobContext(runId, IngestionJobs.CrawlWebsite, websiteId.ToString(), 1, 3) { Steps = new RecordingSteps(steps, StepIds) };
            await job.HandleAsync(context, Ct);
        });

    public Source Website(Guid id) => Context.Repository.Find(id)!;

    public async Task<List<Source>> PagesAsync(Guid websiteId) =>
        [.. (await Context.Repository.ListAsync(Ct)).Where(source => source.ParentId == websiteId).OrderBy(source => source.Url, StringComparer.Ordinal)];

    public ValueTask DisposeAsync() => Context.DisposeAsync();

    private sealed class RecordingSteps(IJobSteps inner, List<string> ids) : IJobSteps
    {
        public Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> step)
        {
            lock (ids)
            {
                ids.Add(id);
            }

            return inner.RunAsync(id, step);
        }
    }
}

internal static class SitePages
{
    public static string Html(string title, string body, params string[] links) =>
        $"""
        <html><head><title>{title}</title></head>
        <body><main><h1>{title}</h1><p>{body}</p>
        {string.Concat(links.Select(link => $"<a href=\"{link}\">{link}</a> "))}
        </main></body></html>
        """;

    public static string Article(string title, params string[] links) =>
        Html(title, TestDocuments.Prose(2, topic: title.ToLowerInvariant()), links);
}
