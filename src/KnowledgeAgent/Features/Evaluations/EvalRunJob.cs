using KnowledgeAgent.Infrastructure.Jobs;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalRunJob(
    EvalRunQueue queue,
    EvalRunExecutor executor,
    IEvalRunRepository runs,
    TimeProvider timeProvider) : IJobHandler
{
    internal const string InterruptedError = "The app restarted while this run was in progress.";

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var runId) || await runs.GetAsync(runId, cancellationToken) is not { } run)
        {
            return;
        }

        if (run.Status == EvalRunStatuses.Running)
        {
            // A previous attempt stopped with the process; a run cannot resume halfway, so it ends as failed.
            run.Status = EvalRunStatuses.Failed;
            run.Error = InterruptedError;
            run.CompletedAt = timeProvider.GetUtcNow();
            await runs.UpdateAsync(run, CancellationToken.None);
            return;
        }

        if (run.Status != EvalRunStatuses.Queued)
        {
            return;
        }

        var cancellation = queue.Register(runId, cancellationToken);
        try
        {
            await executor.ExecuteAsync(runId, cancellation.Token, cancellationToken);
        }
        finally
        {
            queue.Unregister(runId, cancellation);
        }
    }
}
