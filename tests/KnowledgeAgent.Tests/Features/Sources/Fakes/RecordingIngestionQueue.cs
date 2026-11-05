using System.Collections.Concurrent;
using KnowledgeAgent.Features.Sources.Ingestion;

namespace KnowledgeAgent.Tests.Features.Sources.Fakes;

internal sealed class RecordingIngestionQueue : IIngestionQueue
{
    private readonly ConcurrentQueue<QueuedIngestion> _queued = new();

    public int Count => _queued.Count;

    public Task EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        EnqueueAsync(sourceId, TimeSpan.Zero, cancellationToken);

    public Task EnqueueAsync(Guid sourceId, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        _queued.Enqueue(new QueuedIngestion(sourceId, delay));
        return Task.CompletedTask;
    }

    public ConcurrentQueue<Guid> Crawls { get; } = new();

    public Task EnqueueCrawlAsync(Guid websiteId, CancellationToken cancellationToken = default)
    {
        Crawls.Enqueue(websiteId);
        return Task.CompletedTask;
    }

    public List<QueuedIngestion> Drain()
    {
        var drained = new List<QueuedIngestion>();
        while (_queued.TryDequeue(out var queued))
        {
            drained.Add(queued);
        }

        return drained;
    }
}

internal readonly record struct QueuedIngestion(Guid SourceId, TimeSpan Delay);
