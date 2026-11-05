using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Infrastructure.Jobs;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class EvalRunQueueTests
{
    [Fact]
    public void A_run_that_ends_does_not_unregister_a_later_run_of_the_same_id()
    {
        var queue = new EvalRunQueue(new NoJobs());
        var runId = Guid.NewGuid();
        var first = queue.Register(runId, CancellationToken.None);
        var second = queue.Register(runId, CancellationToken.None);

        queue.Unregister(runId, first);

        queue.TryCancel(runId).ShouldBeTrue();
        second.IsCancellationRequested.ShouldBeTrue();
        queue.Unregister(runId, second);
        queue.TryCancel(runId).ShouldBeFalse();
    }

    private sealed class NoJobs : IJobQueue
    {
        public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
