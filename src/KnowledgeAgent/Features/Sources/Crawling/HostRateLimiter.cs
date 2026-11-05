using System.Collections.Concurrent;

namespace KnowledgeAgent.Features.Sources.Crawling;

// Spaces requests to each host across every crawl in the process. A caller holds the host's turn while it waits,
// so concurrent crawls of one host queue up instead of bursting.
internal sealed class HostRateLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, HostTurn> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask WaitAsync(string host, TimeSpan interval, CancellationToken cancellationToken)
    {
        var turn = _hosts.GetOrAdd(host, _ => new HostTurn());
        await turn.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = turn.NextAt - timeProvider.GetTimestamp();
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds((double)wait / timeProvider.TimestampFrequency), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }

            turn.NextAt = timeProvider.GetTimestamp() + (long)(interval.TotalSeconds * timeProvider.TimestampFrequency);
        }
        finally
        {
            turn.Gate.Release();
        }
    }

    private sealed class HostTurn
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public long NextAt { get; set; }
    }
}
