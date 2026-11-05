using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Sources.Ingestion;

internal static class IngestionJobs
{
    public const string IngestSource = "ingestion.source";
    public const string RefreshWebSources = "ingestion.refresh-web";
    public const string CrawlWebsite = "ingestion.crawl-website";
    public const int MaxConcurrentCrawls = 4;
    public const string RefreshWebSourcesSchedule = "refresh-web-sources";
    public const int MaxConcurrency = 64;
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
}

internal sealed class IngestionQueue(IJobQueue jobs, TimeProvider timeProvider) : IIngestionQueue
{
    public Task EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        jobs.EnqueueAsync(IngestionJobs.IngestSource, sourceId.ToString(), cancellationToken: cancellationToken);

    public Task EnqueueAsync(Guid sourceId, TimeSpan delay, CancellationToken cancellationToken = default) =>
        jobs.EnqueueAsync(IngestionJobs.IngestSource, sourceId.ToString(), timeProvider.GetUtcNow() + delay, cancellationToken);

    public Task EnqueueCrawlAsync(Guid websiteId, CancellationToken cancellationToken = default) =>
        jobs.EnqueueAsync(IngestionJobs.CrawlWebsite, websiteId.ToString(), cancellationToken: cancellationToken);
}

internal sealed partial class IngestSourceJob(
    IIngestionPipeline pipeline,
    ISourceRepository repository,
    TimeProvider timeProvider,
    ILogger<IngestSourceJob> logger) : IJobHandler, IJobFailureHandler
{
    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) =>
        Guid.TryParse(context.Key, out var sourceId) ? pipeline.ProcessAsync(sourceId, context.JobId, cancellationToken) : Task.CompletedTask;

    // The pipeline records its own failures; this covers the job failing around it (the database was unreachable when
    // it started or finished), which would otherwise leave the source Pending or Processing with nothing queued.
    public async Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(failure.Key, out var sourceId)
            || await repository.GetAsync(sourceId, cancellationToken) is not { Status: SourceStatus.Pending or SourceStatus.Processing } source)
        {
            return;
        }

        source.Status = SourceStatus.Failed;
        source.Stage = null;
        source.Error = ErrorSanitizer.Sanitize(failure.Error);
        source.UpdatedAt = timeProvider.GetUtcNow();
        await repository.UpdateAsync(source, cancellationToken);
        LogMarkedFailed(logger, sourceId, failure.Attempts);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {SourceId} was marked failed after its job failed {Attempts} times")]
    private static partial void LogMarkedFailed(ILogger logger, Guid sourceId, int attempts);
}

internal sealed partial class RefreshWebSourcesJob(
    ISourceRepository repository,
    ISourceService sources,
    IIngestionQueue queue,
    ILogger<RefreshWebSourcesJob> logger) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        // Collected first: re-ingesting moves sources out of the Ready filter and would shift later pages. A website's
        // pages are left to its crawl.
        var pages = await CollectAsync(new SourceQuery(Type: SourceType.Url, Statuses: [SourceStatus.Ready], Parent: ParentFilter.TopLevel), cancellationToken);
        var websites = await CollectAsync(new SourceQuery(Type: SourceType.Website, Statuses: [SourceStatus.Ready, SourceStatus.Failed]), cancellationToken);

        foreach (var id in pages)
        {
            await sources.ReingestAsync(id, cancellationToken);
        }

        foreach (var id in websites)
        {
            await queue.EnqueueCrawlAsync(id, cancellationToken);
        }

        LogRefreshed(logger, pages.Count, websites.Count);
    }

    private async Task<List<Guid>> CollectAsync(SourceQuery query, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        query = query with { Limit = SourceQuery.MaxLimit };
        while (true)
        {
            var page = await repository.FindAsync(query, cancellationToken);
            ids.AddRange(page.Items.Select(source => source.Id));
            if (page.NextOffset is not { } next)
            {
                return ids;
            }

            query = query with { Offset = next };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Pages} web pages and {Websites} websites for a refresh")]
    private static partial void LogRefreshed(ILogger logger, int pages, int websites);
}
