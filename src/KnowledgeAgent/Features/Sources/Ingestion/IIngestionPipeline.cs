namespace KnowledgeAgent.Features.Sources.Ingestion;

public interface IIngestionPipeline
{
    Task ProcessAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        ProcessAsync(sourceId, jobRunId: null, cancellationToken);

    Task ProcessAsync(Guid sourceId, string? jobRunId, CancellationToken cancellationToken = default);
}
