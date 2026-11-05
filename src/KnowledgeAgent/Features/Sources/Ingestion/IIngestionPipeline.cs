namespace KnowledgeAgent.Features.Sources.Ingestion;

public interface IIngestionPipeline
{
    Task ProcessAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        ProcessAsync(sourceId, jobRunId: null, cancellationToken);

    // jobRunId is the job engine's run, recorded on the source; null when the pipeline runs outside a job.
    Task ProcessAsync(Guid sourceId, string? jobRunId, CancellationToken cancellationToken = default);
}
