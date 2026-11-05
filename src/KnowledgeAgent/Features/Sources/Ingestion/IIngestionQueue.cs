namespace KnowledgeAgent.Features.Sources.Ingestion;

public interface IIngestionQueue
{
    Task EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task EnqueueAsync(Guid sourceId, TimeSpan delay, CancellationToken cancellationToken = default);

    Task EnqueueCrawlAsync(Guid websiteId, CancellationToken cancellationToken = default);
}
