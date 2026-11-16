namespace KnowledgeAgent.Infrastructure.Jobs;

public interface IJobHandler
{
    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(string JobId, string Kind, string Key, int Attempt, int MaxAttempts)
{
    public IJobSteps Steps { get; init; } = JobSteps.Inline;
}

public interface IJobFailureHandler
{
    Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken);
}

public sealed record JobFailure(string JobId, string Kind, string Key, int Attempts, string Error);
