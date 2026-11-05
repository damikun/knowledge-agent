using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class InProcessJobRunnerTests
{
    private const string Kind = "test.work";
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queued_job_runs_once_with_its_context()
    {
        var work = new WorkProbe();
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha");

        var context = await work.WaitForCallAsync(1);
        context.Kind.ShouldBe(Kind);
        context.Key.ShouldBe("alpha");
        context.Attempt.ShouldBe(1);
        context.MaxAttempts.ShouldBe(3);
        Guid.TryParse(context.JobId, out _).ShouldBeTrue();
        await Task.Delay(100, Ct);
        work.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_attempts_are_retried_in_the_same_run_until_the_last_attempt()
    {
        var work = new WorkProbe { Behavior = (_, _) => throw new InvalidOperationException("boom") };
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha");

        await work.WaitForCallAsync(3);
        await Task.Delay(100, Ct);
        work.Calls.Select(call => call.Attempt).ShouldBe([1, 2, 3]);
        work.Calls.Select(call => call.JobId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_retry_that_succeeds_ends_the_run()
    {
        var work = new WorkProbe { Behavior = (context, _) => context.Attempt == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask };
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha");

        await work.WaitForCallAsync(2);
        await Task.Delay(100, Ct);
        work.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Running_jobs_never_exceed_the_policy_concurrency()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new WorkProbe { Behavior = (_, ct) => release.Task.WaitAsync(ct) };
        await using var host = await RunnerHost.StartAsync(work, new JobPolicy { MaxConcurrency = 2 });
        string[] keys = ["a", "b", "c", "d", "e"];

        foreach (var key in keys)
        {
            await host.EnqueueAsync(key);
        }

        await WaitUntilAsync(() => work.Running == 2);
        await Task.Delay(100, Ct);
        work.Running.ShouldBe(2);
        release.SetResult();

        await work.WaitForCallAsync(keys.Length);
        work.Calls.Select(call => call.Key).Order().ShouldBe(keys);
        work.MaxRunning.ShouldBe(2);
    }

    [Fact]
    public async Task A_key_queued_while_it_runs_runs_once_more_afterwards_and_never_alongside_itself()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new WorkProbe { Behavior = (_, ct) => release.Task.WaitAsync(ct) };
        await using var host = await RunnerHost.StartAsync(work, new JobPolicy { MaxConcurrency = 4 });
        await host.EnqueueAsync("alpha");
        await work.WaitForCallAsync(1);

        await host.EnqueueAsync("alpha");
        await host.EnqueueAsync("alpha");
        await Task.Delay(100, Ct);
        work.Calls.Count.ShouldBe(1);
        release.SetResult();

        await work.WaitForCallAsync(2);
        await Task.Delay(100, Ct);
        work.Calls.Count.ShouldBe(2);
        work.Calls[1].JobId.ShouldNotBe(work.Calls[0].JobId);
        work.MaxRunning.ShouldBe(1);
    }

    [Fact]
    public async Task A_skipping_kind_drops_requests_for_a_key_that_runs()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new WorkProbe { Behavior = (_, ct) => release.Task.WaitAsync(ct) };
        await using var host = await RunnerHost.StartAsync(work, new JobPolicy { MaxConcurrency = 4, Overlap = JobOverlap.Skip });
        await host.EnqueueAsync("poll");
        await work.WaitForCallAsync(1);

        await host.EnqueueAsync("poll");
        await host.EnqueueAsync("poll");
        await Task.Delay(100, Ct);
        release.SetResult();
        await Task.Delay(200, Ct);
        work.Calls.Count.ShouldBe(1);

        await host.EnqueueAsync("poll");
        await work.WaitForCallAsync(2);
    }

    [Fact]
    public async Task An_idempotent_kind_runs_a_key_once_per_window()
    {
        var work = new WorkProbe();
        await using var host = await RunnerHost.StartAsync(work, new JobPolicy { MaxConcurrency = 4, Idempotent = true });

        await host.EnqueueAsync("dispatch");
        await work.WaitForCallAsync(1);
        await host.EnqueueAsync("dispatch");
        await host.EnqueueAsync("dispatch", Start.AddMinutes(5));
        host.Time.Advance(TimeSpan.FromMinutes(5));
        await host.EnqueueAsync("other");
        await work.WaitForCallAsync(2);
        await Task.Delay(100, Ct);
        work.Calls.Select(call => call.Key).ShouldBe(["dispatch", "other"]);

        host.Time.Advance(JobPolicy.IdempotencyWindow);
        await host.EnqueueAsync("dispatch");
        (await work.WaitForCallAsync(3)).Key.ShouldBe("dispatch");
    }

    [Fact]
    public async Task A_debounced_kind_runs_a_burst_once_when_it_goes_quiet()
    {
        var work = new WorkProbe();
        await using var host = await RunnerHost.StartAsync(work, new JobPolicy { MaxConcurrency = 4, Debounce = TimeSpan.FromSeconds(2) });

        await host.EnqueueAsync("source");
        host.Time.Advance(TimeSpan.FromSeconds(1));
        await host.EnqueueAsync("source");
        host.Time.Advance(TimeSpan.FromSeconds(1));
        await host.EnqueueAsync("source");
        host.Time.Advance(TimeSpan.FromSeconds(1.5));
        await Task.Delay(100, Ct);
        work.Calls.ShouldBeEmpty();

        host.Time.Advance(TimeSpan.FromSeconds(0.5));

        await work.WaitForCallAsync(1);
        await Task.Delay(100, Ct);
        work.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_failure_hook_runs_once_after_the_last_attempt()
    {
        var work = new WorkProbe { Behavior = (_, _) => throw new InvalidOperationException("Slack is down.") };
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha");

        var failure = await work.WaitForFailureAsync();
        work.Calls.Count.ShouldBe(3);
        failure.ShouldBe(new JobFailure(work.Calls[0].JobId, Kind, "alpha", 3, "Slack is down."));
        await Task.Delay(100, Ct);
        work.Failures.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_job_that_succeeds_on_a_retry_does_not_call_the_failure_hook()
    {
        var work = new WorkProbe { Behavior = (context, _) => context.Attempt == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask };
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha");

        await work.WaitForCallAsync(2);
        await Task.Delay(100, Ct);
        work.Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delayed_job_runs_when_it_becomes_due()
    {
        var work = new WorkProbe();
        await using var host = await RunnerHost.StartAsync(work);

        await host.EnqueueAsync("alpha", Start.AddMinutes(1));
        host.Time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(100, Ct);
        work.Calls.ShouldBeEmpty();

        host.Time.Advance(TimeSpan.FromSeconds(1));

        (await work.WaitForCallAsync(1)).Key.ShouldBe("alpha");
    }

    [Fact]
    public async Task Stopping_cancels_a_running_job_without_retrying_it()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new WorkProbe { Behavior = (_, ct) => WaitForCancellationAsync(cancelled, ct) };
        await using var host = await RunnerHost.StartAsync(work);
        await host.EnqueueAsync("alpha");
        await work.WaitForCallAsync(1);

        await host.StopAsync();

        await cancelled.Task.WaitAsync(Timeout, Ct);
        work.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Stopping_after_the_host_was_disposed_does_not_throw()
    {
        // The test host stops the app twice: WebApplicationFactory does, and so does the entry point's app.Run(),
        // which then disposes it while the factory may still be stopping services.
        var host = await RunnerHost.StartAsync(new WorkProbe());
        var runner = host.Runner;
        await host.DisposeAsync();

        await Should.NotThrowAsync(() => runner.StopAsync(Ct));
    }

    [Fact]
    public async Task Enqueueing_a_kind_without_a_handler_fails()
    {
        await using var host = await RunnerHost.StartAsync(new WorkProbe());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.Queue.EnqueueAsync("missing.kind", "alpha", cancellationToken: Ct));

        exception.Message.ShouldContain("missing.kind");
    }

    private static async Task WaitForCancellationAsync(TaskCompletionSource cancelled, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult();
            throw;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RunnerHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private bool _stopped;

        private RunnerHost(IHost host, FakeTimeProvider time)
        {
            _host = host;
            Time = time;
        }

        public FakeTimeProvider Time { get; }

        public IJobQueue Queue => _host.Services.GetRequiredService<IJobQueue>();

        public InProcessJobRunner Runner => _host.Services.GetRequiredService<InProcessJobRunner>();

        public static async Task<RunnerHost> StartAsync(WorkProbe work, JobPolicy? policy = null)
        {
            var time = new FakeTimeProvider(Start);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
            });
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.AddJobs();
            builder.Services.AddSingleton(work);
            builder.Services.AddJob<WorkJob>(Kind, policy ?? new JobPolicy { MaxAttempts = 3 });

            var host = builder.Build();
            await host.StartAsync(Ct);
            return new RunnerHost(host, time);
        }

        public Task EnqueueAsync(string key, DateTimeOffset? runAt = null) => Queue.EnqueueAsync(Kind, key, runAt, Ct);

        public async Task StopAsync()
        {
            _stopped = true;
            await _host.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stopped)
            {
                await StopAsync();
            }

            _host.Dispose();
        }
    }

    private sealed class WorkProbe
    {
        private readonly ConcurrentQueue<JobContext> _calls = new();
        private readonly ConcurrentQueue<JobFailure> _failures = new();
        private int _running;
        private int _maxRunning;

        public Func<JobContext, CancellationToken, Task> Behavior { get; init; } = (_, _) => Task.CompletedTask;

        public IReadOnlyList<JobContext> Calls => [.. _calls];

        public IReadOnlyList<JobFailure> Failures => [.. _failures];

        public void RecordFailure(JobFailure failure) => _failures.Enqueue(failure);

        public async Task<JobFailure> WaitForFailureAsync()
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (_failures.IsEmpty)
            {
                await Task.Delay(10, timeout.Token);
            }

            return Failures[0];
        }

        public int Running => Volatile.Read(ref _running);

        public int MaxRunning => Volatile.Read(ref _maxRunning);

        public async Task RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            _calls.Enqueue(context);
            var running = Interlocked.Increment(ref _running);
            int max;
            while (running > (max = Volatile.Read(ref _maxRunning)) && Interlocked.CompareExchange(ref _maxRunning, running, max) != max)
            {
            }

            try
            {
                await Behavior(context, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public async Task<JobContext> WaitForCallAsync(int count)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (_calls.Count < count)
            {
                await Task.Delay(10, timeout.Token);
            }

            return Calls[count - 1];
        }
    }

    private sealed class WorkJob(WorkProbe probe) : IJobHandler, IJobFailureHandler
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => probe.RunAsync(context, cancellationToken);

        public Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
        {
            probe.RecordFailure(failure);
            return Task.CompletedTask;
        }
    }
}
