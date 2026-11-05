using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Inngest.Testing.Containers;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class InngestDevServerTests(InngestDevServerFixture fixture) : IClassFixture<InngestDevServerFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_queued_job_runs_through_the_dev_server()
    {
        var queue = fixture.Queue;

        await queue.EnqueueAsync(InngestDevServerFixture.Kind, "alpha", cancellationToken: Ct);

        var job = (await fixture.Recorder.WaitForAsync("alpha", calls: 1, Timeout, Ct)).ShouldHaveSingleItem();
        job.Context.Kind.ShouldBe(InngestDevServerFixture.Kind);
        job.Context.Attempt.ShouldBe(1);
        job.Context.MaxAttempts.ShouldBe(3);
        job.Context.JobId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_failing_job_is_retried_by_inngest_until_it_succeeds()
    {
        fixture.Recorder.FailFirst("flaky", attempts: 2);

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.Kind, "flaky", cancellationToken: Ct);

        var calls = await fixture.Recorder.WaitForAsync("flaky", calls: 3, Timeout, Ct);
        calls.Select(call => call.Context.Attempt).ShouldBe([1, 2, 3]);
        calls.Select(call => call.Context.JobId).Distinct().ShouldHaveSingleItem("the retries belong to one run");
    }

    [Fact]
    public async Task A_job_queued_for_later_starts_at_its_time()
    {
        var queuedAt = DateTimeOffset.UtcNow;

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.Kind, "later", queuedAt.AddSeconds(4), Ct);

        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        fixture.Recorder.Calls("later").ShouldBeEmpty();
        var job = (await fixture.Recorder.WaitForAsync("later", calls: 1, Timeout, Ct)).ShouldHaveSingleItem();
        job.StartedAt.ShouldBeGreaterThanOrEqualTo(queuedAt.AddSeconds(3.5));
    }

    [Fact]
    public async Task Runs_for_the_same_key_never_overlap()
    {
        fixture.Recorder.Delay("serial", TimeSpan.FromSeconds(1));

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.Kind, "serial", cancellationToken: Ct);
        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.Kind, "serial", cancellationToken: Ct);

        var calls = await fixture.Recorder.WaitForAsync("serial", calls: 2, Timeout, Ct);
        calls.Select(call => call.Context.JobId).Distinct().Count().ShouldBe(2);
        fixture.Recorder.MaxConcurrent("serial").ShouldBe(1);
    }

    [Fact]
    public async Task The_inngest_health_check_passes_against_the_dev_server()
    {
        var report = await fixture.CheckHealthAsync();

        report.Entries[JobsModule.InngestHealthCheckName].Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task An_idempotent_kind_runs_a_key_once_however_often_it_is_queued()
    {
        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.OnceKind, "once", cancellationToken: Ct);
        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.OnceKind, "once", cancellationToken: Ct);

        await fixture.Recorder.WaitForAsync("once", calls: 1, Timeout, Ct);
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        fixture.Recorder.Calls("once").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_skipping_kind_drops_requests_for_a_key_that_runs()
    {
        fixture.Recorder.Delay("skip", TimeSpan.FromSeconds(3));
        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.SkipKind, "skip", cancellationToken: Ct);
        await fixture.Recorder.WaitForAsync("skip", calls: 1, Timeout, Ct);

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.SkipKind, "skip", cancellationToken: Ct);
        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.SkipKind, "skip", cancellationToken: Ct);

        await Task.Delay(TimeSpan.FromSeconds(6), Ct);
        fixture.Recorder.Calls("skip").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_debounced_kind_runs_a_burst_of_requests_once()
    {
        for (var i = 0; i < 3; i++)
        {
            await fixture.Queue.EnqueueAsync(InngestDevServerFixture.DebounceKind, "burst", cancellationToken: Ct);
        }

        await fixture.Recorder.WaitForAsync("burst", calls: 1, Timeout, Ct);
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);
        fixture.Recorder.Calls("burst").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_debounced_job_queued_for_later_still_waits_for_its_time()
    {
        var queuedAt = DateTimeOffset.UtcNow;

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.DebounceKind, "debounced-later", queuedAt.AddSeconds(8), Ct);

        var job = (await fixture.Recorder.WaitForAsync("debounced-later", calls: 1, Timeout, Ct)).ShouldHaveSingleItem();
        job.StartedAt.ShouldBeGreaterThanOrEqualTo(queuedAt.AddSeconds(7.5));
    }

    [Fact]
    public async Task A_job_that_fails_for_good_calls_the_failure_hook_with_its_key()
    {
        fixture.Recorder.FailFirst("doomed", attempts: 100);

        await fixture.Queue.EnqueueAsync(InngestDevServerFixture.FailingKind, "doomed", cancellationToken: Ct);

        var failure = await fixture.Recorder.WaitForFailureAsync("doomed", Timeout, Ct);
        failure.Kind.ShouldBe(InngestDevServerFixture.FailingKind);
        failure.Error.ShouldContain("failed on purpose");
        fixture.Recorder.Calls("doomed").Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_enabled_schedule_queues_its_job_from_an_inngest_cron_trigger()
    {
        var functions = await fixture.GetFunctionIdsAsync();
        functions.ShouldContain($"{InngestDevServerFixture.AppId}-schedule-every-minute");
        functions.ShouldNotContain($"{InngestDevServerFixture.AppId}-schedule-paused");

        // The trigger fires at the next full minute.
        var job = (await fixture.Recorder.WaitForAsync("every-minute", calls: 1, TimeSpan.FromSeconds(80), Ct))[0];
        job.Context.Kind.ShouldBe(InngestDevServerFixture.Kind);
        fixture.Recorder.Calls("paused").ShouldBeEmpty();
    }
}

public sealed class InngestDevServerFixture : IAsyncLifetime
{
    public const string Kind = "test.work";
    public const string OnceKind = "test.once";
    public const string SkipKind = "test.skip";
    public const string DebounceKind = "test.debounce";
    public const string FailingKind = "test.failing";
    public const string AppId = "knowledge-agent-tests";

    private InngestServerContainer? _devServer;
    private WebApplication? _app;

    public JobRecorder Recorder { get; } = new();

    public IJobQueue Queue
    {
        get
        {
            Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
            return _app!.Services.GetRequiredService<IJobQueue>();
        }
    }

    public Task<HealthReport> CheckHealthAsync()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        return _app!.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetFunctionIdsAsync()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        using var http = new HttpClient { BaseAddress = _devServer!.GetBaseAddress() };
        using var body = JsonDocument.Parse(await http.GetStringAsync($"v1/apps/{AppId}/functions"));
        return [.. body.RootElement.EnumerateArray().Select(function => function.GetProperty("id").GetString() ?? string.Empty)];
    }

    public async ValueTask InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        // The server starts first because the app needs its address. Its fast timing syncs every second and retries
        // failed runs after a second instead of the production backoff.
        _devServer = new InngestServerBuilder(ContainerImages.Inngest).Build();
        await _devServer.StartAsync();

        _app = BuildApp(_devServer);
        await _app.StartAsync();

        // Events sent before the dev server knows the function would start no run.
        await _devServer.SyncAsync(new Uri(new Uri(_app.Urls.First()), "/api/inngest"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_devServer is not null)
        {
            await _devServer.DisposeAsync();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private WebApplication BuildApp(InngestServerContainer devServer)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        // All interfaces, not loopback: the container reaches the app through the Docker bridge on Linux.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(0));
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceModule.InMemoryProvider,
            ["Persistence:InMemoryDatabaseName"] = $"inngest-dev-server-{Guid.NewGuid():N}",
            ["Jobs:Provider"] = JobEngines.Inngest,
            ["Jobs:Inngest:AppId"] = AppId,
        });
        builder.Configuration.AddInMemoryCollection(devServer.GetSdkEnvironment()!);
        builder.Services.AddSingleton(Recorder);
        builder.AddPersistence();
        builder.AddJobs();
        builder.Services.AddJob<RecordingJob>(Kind, new JobPolicy { MaxConcurrency = 4, MaxAttempts = 3 });
        builder.Services.AddJob<RecordingJob>(OnceKind, new JobPolicy { MaxConcurrency = 4, Idempotent = true });
        builder.Services.AddJob<RecordingJob>(SkipKind, new JobPolicy { MaxConcurrency = 4, Overlap = JobOverlap.Skip });
        builder.Services.AddJob<RecordingJob>(DebounceKind, new JobPolicy { MaxConcurrency = 4, Debounce = TimeSpan.FromSeconds(2) });
        builder.Services.AddJob<FailureRecordingJob>(FailingKind, new JobPolicy { MaxConcurrency = 4, MaxAttempts = 2 });
        builder.Services.AddJobSchedule(new JobScheduleRegistration("every-minute", Kind, "every-minute", "* * * * *", Enabled: true));
        builder.Services.AddJobSchedule(new JobScheduleRegistration("paused", Kind, "paused", "* * * * *", Enabled: false));

        var app = builder.Build();
        app.MapGroup("/api").MapInngest(InngestJobEngine.EndpointPath);
        return app;
    }
}

public sealed record RecordedJob(JobContext Context, DateTimeOffset StartedAt);

public sealed class JobRecorder
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<RecordedJob>> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _failuresLeft = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TimeSpan> _delays = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _maxRunning = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, JobFailure> _failures = new(StringComparer.Ordinal);

    public void FailFirst(string key, int attempts) => _failuresLeft[key] = attempts;

    public void RecordFailure(JobFailure failure) => _failures[failure.Key] = failure;

    public async Task<JobFailure> WaitForFailureAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!_failures.ContainsKey(key))
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Job '{key}' did not report a failure within {timeout.TotalSeconds:0} s.");
            }

            await Task.Delay(50, cancellationToken);
        }

        return _failures[key];
    }

    public void Delay(string key, TimeSpan delay) => _delays[key] = delay;

    public IReadOnlyList<RecordedJob> Calls(string key) => _calls.TryGetValue(key, out var calls) ? [.. calls] : [];

    public int MaxConcurrent(string key) => _maxRunning.GetValueOrDefault(key);

    public async Task<IReadOnlyList<RecordedJob>> WaitForAsync(string key, int calls, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (Calls(key).Count < calls)
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Job '{key}' ran {Calls(key).Count} times instead of {calls} within {timeout.TotalSeconds:0} s.");
            }

            await Task.Delay(50, cancellationToken);
        }

        return Calls(key);
    }

    public async Task RunAsync(JobContext context, CancellationToken cancellationToken)
    {
        var running = _running.AddOrUpdate(context.Key, 1, (_, count) => count + 1);
        _maxRunning.AddOrUpdate(context.Key, running, (_, max) => Math.Max(max, running));
        try
        {
            _calls.GetOrAdd(context.Key, _ => new ConcurrentQueue<RecordedJob>()).Enqueue(new RecordedJob(context, DateTimeOffset.UtcNow));
            if (_delays.TryGetValue(context.Key, out var delay))
            {
                await Task.Delay(delay, cancellationToken);
            }

            if (_failuresLeft.TryGetValue(context.Key, out var left) && left > 0 && _failuresLeft.TryUpdate(context.Key, left - 1, left))
            {
                throw new InvalidOperationException($"Attempt {context.Attempt} of job '{context.Key}' failed on purpose.");
            }
        }
        finally
        {
            _running.AddOrUpdate(context.Key, 0, (_, count) => count - 1);
        }
    }
}

public sealed class RecordingJob(JobRecorder recorder) : IJobHandler
{
    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => recorder.RunAsync(context, cancellationToken);
}

public sealed class FailureRecordingJob(JobRecorder recorder) : IJobHandler, IJobFailureHandler
{
    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => recorder.RunAsync(context, cancellationToken);

    public Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        recorder.RecordFailure(failure);
        return Task.CompletedTask;
    }
}
