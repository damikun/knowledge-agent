namespace KnowledgeAgent.Infrastructure.Jobs;

public interface IJobHandler
{
    Task HandleAsync(JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(string JobId, string Kind, string Key, int Attempt, int MaxAttempts)
{
    public IJobSteps Steps { get; init; } = JobSteps.Inline;
}

// Called once after a job's last attempt failed, but not for jobs stopped by a shutdown.
// Inngest runs it as the function's onFailure handler ("<id>-failure").
public interface IJobFailureHandler
{
    Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken);
}

public sealed record JobFailure(string JobId, string Kind, string Key, int Attempts, string Error);
