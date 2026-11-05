namespace KnowledgeAgent.Infrastructure.Jobs;

// Both engines must honor each setting the same way (see InngestJobEngine.CreateOptions).
public sealed record JobPolicy
{
    public static readonly JobPolicy Default = new();

    // Inngest's idempotency period, which is not configurable.
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    public int MaxConcurrency { get; init; } = 1;
    public int MaxAttempts { get; init; } = 5;

    // A key runs at most once per IdempotencyWindow, for one-shot work such as sending a handover email.
    // In process the keys are kept in memory and lost on restart.
    public bool Idempotent { get; init; }

    public JobOverlap Overlap { get; init; } = JobOverlap.Queue;

    // Waits for a quiet period per key so a burst costs one run. Inngest's minimum is one second.
    public TimeSpan? Debounce { get; init; }
}

public enum JobOverlap
{
    // Runs after the running one, never alongside it. Fine for jobs that work from the current state.
    Queue,

    // Dropped while the key runs; for polled work where the next tick does the same.
    Skip,
}
