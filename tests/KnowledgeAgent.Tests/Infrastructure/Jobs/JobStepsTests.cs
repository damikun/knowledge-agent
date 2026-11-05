using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class JobStepsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InProcessJobSteps Steps(int maxAttempts, InstantTimeProvider? clock = null) =>
        new(maxAttempts, clock ?? new InstantTimeProvider(), NullLogger.Instance, Ct);

    [Fact]
    public async Task A_step_that_succeeded_is_not_run_again()
    {
        var steps = Steps(3);
        var runs = 0;

        var first = await steps.RunAsync("a", _ => Task.FromResult(++runs));
        var second = await steps.RunAsync("a", _ => Task.FromResult(++runs));

        first.ShouldBe(1);
        second.ShouldBe(1);
        runs.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_step_is_retried_with_backoff_and_then_reported()
    {
        var clock = new InstantTimeProvider();
        var steps = Steps(3, clock);
        var runs = 0;

        var failure = await Should.ThrowAsync<JobStepFailedException>(() => steps.RunAsync<int>("flaky", _ =>
        {
            runs++;
            throw new InvalidOperationException("still broken");
        }));

        failure.StepId.ShouldBe("flaky");
        failure.Message.ShouldBe("still broken");
        runs.ShouldBe(3);
        clock.Elapsed.ShouldBeGreaterThan(TimeSpan.Zero);

        // Like a replay on Inngest, a later call reports the failure without running the step again.
        await Should.ThrowAsync<JobStepFailedException>(() => steps.RunAsync("flaky", _ => Task.FromResult(++runs)));
        runs.ShouldBe(3);
    }

    [Fact]
    public async Task A_step_that_recovers_returns_its_result()
    {
        var steps = Steps(3);
        var runs = 0;

        var result = await steps.RunAsync("recovers", _ => ++runs < 2 ? throw new IOException("blip") : Task.FromResult("ok"));

        result.ShouldBe("ok");
        runs.ShouldBe(2);
    }

    [Fact]
    public async Task Inline_steps_run_once_and_wrap_failures()
    {
        (await JobSteps.Inline.RunAsync("x", _ => Task.FromResult(7))).ShouldBe(7);
        await Should.ThrowAsync<JobStepFailedException>(() => JobSteps.Inline.RunAsync<int>("y", _ => throw new InvalidOperationException("no")));
    }
}
