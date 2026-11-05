using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.Jobs;

namespace KnowledgeAgent.Features.Evaluations;

public sealed class EvalRunQueue(IJobQueue jobs)
{
    public const string JobKind = "evaluations.run";

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public Task EnqueueAsync(Guid runId, CancellationToken cancellationToken = default) =>
        jobs.EnqueueAsync(JobKind, runId.ToString(), cancellationToken: cancellationToken);

    internal CancellationTokenSource Register(Guid runId, CancellationToken stopping)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[runId] = source;
        return source;
    }

    // Removes only this source, so a later run of the same id that registered meanwhile stays cancellable.
    internal void Unregister(Guid runId, CancellationTokenSource source)
    {
        _running.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(runId, source));
        source.Dispose();
    }

    internal bool TryCancel(Guid runId)
    {
        if (!_running.TryGetValue(runId, out var source))
        {
            return false;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }
}
