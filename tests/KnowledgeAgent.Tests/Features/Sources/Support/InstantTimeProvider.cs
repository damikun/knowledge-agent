using System.Diagnostics;

namespace KnowledgeAgent.Tests.Features.Sources.Support;

// A clock that jumps ahead instead of waiting: a timer fires right away and moves the clock by its due time, so
// tests can check how long something would have waited without waiting for it.
internal sealed class InstantTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private long _ticks;

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromTicks(_ticks);
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => KnowledgeTestContext.Start + Elapsed;

    public override long GetTimestamp() => (long)(Elapsed.TotalSeconds * Stopwatch.Frequency);

    public override long TimestampFrequency => Stopwatch.Frequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_gate)
            {
                _ticks += Math.Max(0, dueTime.Ticks);
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
