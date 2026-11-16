namespace KnowledgeAgent.Infrastructure.Jobs;

public sealed record JobPolicy
{
    public static readonly JobPolicy Default = new();

    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    public int MaxConcurrency { get; init; } = 1;
    public int MaxAttempts { get; init; } = 5;

    public bool Idempotent { get; init; }

    public JobOverlap Overlap { get; init; } = JobOverlap.Queue;

    public TimeSpan? Debounce { get; init; }
}

public enum JobOverlap
{
    Queue,

    Skip,
}
