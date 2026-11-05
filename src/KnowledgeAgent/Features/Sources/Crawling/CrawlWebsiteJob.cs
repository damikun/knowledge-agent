using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Sources.Crawling;

// A discovery step, one step per page and a finishing step, so a failing page fails alone and a retried run skips the
// finished ones. The code between steps uses only their results, so a replay visits the same pages in the same order.
internal sealed partial class CrawlWebsiteJob(
    WebsiteCrawler crawler,
    ISourceRepository repository,
    TimeProvider timeProvider,
    ILogger<CrawlWebsiteJob> logger) : IJobHandler, IJobFailureHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var websiteId))
        {
            return;
        }

        var runId = context.JobId;
        CrawlPlan plan;
        try
        {
            plan = await context.Steps.RunAsync("discover", token => crawler.DiscoverAsync(websiteId, runId, token));
        }
        catch (JobStepFailedException ex)
        {
            await FinishAsync(context, websiteId, ex.Message, []);
            return;
        }

        if (!plan.Found)
        {
            return;
        }

        if (plan.Error is not null)
        {
            await FinishAsync(context, websiteId, plan.Error, []);
            return;
        }

        var queue = new Queue<PlannedPage>(plan.Pages);
        var seen = new HashSet<string>(plan.Pages.Select(page => page.Url), StringComparer.Ordinal);
        var failed = new List<FailedPage>();
        var followLinks = plan.Discovery == CrawlDiscovery.Links;
        while (queue.TryDequeue(out var page))
        {
            var collectLinks = followLinks && page.Depth < WebsiteCrawler.MaxDepth && seen.Count < plan.MaxPages;
            var request = new PageRequest(websiteId, runId, plan, page, collectLinks, seen.Count);
            PageResult result;
            try
            {
                result = await context.Steps.RunAsync($"page {page.Url}", token => crawler.ProcessPageAsync(request, token));
            }
            catch (JobStepFailedException ex)
            {
                failed.Add(new FailedPage(page.Url, ex.Message));
                continue;
            }

            foreach (var link in result.Links)
            {
                if (seen.Count >= plan.MaxPages)
                {
                    break;
                }

                if (seen.Add(link))
                {
                    queue.Enqueue(new PlannedPage(link, null, page.Depth + 1));
                }
            }
        }

        await FinishAsync(context, websiteId, error: null, failed);
    }

    public async Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(failure.Key, out var websiteId)
            || await repository.GetAsync(websiteId, cancellationToken) is not { Status: SourceStatus.Pending or SourceStatus.Processing } website)
        {
            return;
        }

        website.Status = SourceStatus.Failed;
        website.Stage = null;
        website.Error = ErrorSanitizer.Sanitize(failure.Error);
        website.UpdatedAt = timeProvider.GetUtcNow();
        await repository.UpdateAsync(website, cancellationToken);
        LogMarkedFailed(logger, websiteId, failure.Attempts);
    }

    private Task FinishAsync(JobContext context, Guid websiteId, string? error, IReadOnlyList<FailedPage> failed) =>
        context.Steps.RunAsync("finish", token => crawler.FinishAsync(websiteId, context.JobId, error, failed, token));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Website {WebsiteId} was marked failed after its crawl failed {Attempts} times")]
    private static partial void LogMarkedFailed(ILogger logger, Guid websiteId, int attempts);
}
