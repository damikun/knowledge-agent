using System.Collections.Concurrent;
using Inngest;
using Inngest.Testing;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class InngestJobEngineTests
{
    private const string Kind = "test.work";
    private const string FunctionId = "test-work";
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_inngest_provider_runs_jobs_and_schedules_as_functions_instead_of_in_process()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["INNGEST_DEV"] = "1" });
        var services = host.Services;

        services.GetRequiredService<IJobQueue>().ShouldBeOfType<InngestJobQueue>();
        services.GetServices<IHostedService>().OfType<InProcessJobRunner>().ShouldBeEmpty();
        services.GetServices<IHostedService>().OfType<InProcessScheduler>().ShouldBeEmpty();
        services.GetService<InngestJobEngine>().ShouldNotBeNull();
        services.GetRequiredService<JobEngineInfo>().ShouldBe(new JobEngineInfo("inngest", InngestJobEngine.DefaultDevServerUrl));
    }

    [Fact]
    public void Each_job_kind_becomes_a_function_with_the_limits_of_its_policy()
    {
        var options = InngestJobEngine.CreateOptions(
            new JobRegistration("ingestion.source", typeof(WorkHandler), new JobPolicy { MaxConcurrency = 4, MaxAttempts = 3 }));

        options.Id.ShouldBe("ingestion-source");
        options.Name.ShouldBe("ingestion.source");
        options.Triggers.ShouldHaveSingleItem().ShouldBe(new EventTrigger("jobs/ingestion.source"));
        options.Concurrency.ShouldBe([new ConcurrencyLimit(4), new ConcurrencyLimit(1) { Key = "event.data.key" }]);
        options.Retries.ShouldBe(2);
    }

    [Fact]
    public void The_default_policy_adds_no_flow_control_beyond_the_concurrency_limits()
    {
        var options = InngestJobEngine.CreateOptions(new JobRegistration(Kind, typeof(WorkHandler), JobPolicy.Default));

        options.Idempotency.ShouldBeNull();
        options.Singleton.ShouldBeNull();
        options.Debounce.ShouldBeNull();
    }

    [Fact]
    public void Idempotency_skipping_and_debouncing_map_to_inngest_flow_control_on_the_job_key()
    {
        InngestJobEngine.CreateOptions(new JobRegistration(Kind, typeof(WorkHandler), new JobPolicy { Idempotent = true }))
            .Idempotency.ShouldBe("event.data.key");
        InngestJobEngine.CreateOptions(new JobRegistration(Kind, typeof(WorkHandler), new JobPolicy { Overlap = JobOverlap.Skip }))
            .Singleton.ShouldBe(new Singleton(SingletonMode.Skip) { Key = "event.data.key" });
        InngestJobEngine.CreateOptions(new JobRegistration(Kind, typeof(WorkHandler), new JobPolicy { Debounce = TimeSpan.FromSeconds(2) }))
            .Debounce.ShouldBe(new Debounce(TimeSpan.FromSeconds(2)) { Key = "event.data.key" });
        InngestJobEngine.CreateOptions(new JobRegistration(Kind, typeof(WorkHandler), new JobPolicy { Debounce = TimeSpan.FromMilliseconds(200) }))
            .Debounce.ShouldBe(new Debounce(TimeSpan.FromSeconds(1)) { Key = "event.data.key" }, "Inngest's shortest period");
    }

    [Fact]
    public async Task A_job_that_fails_for_good_runs_the_handlers_failure_hook_with_its_key()
    {
        var recorder = new JobRecorder { Failure = new InvalidOperationException("Slack is down.") };
        await using var host = CreateTestHost(recorder, handlerType: typeof(FailureAwareHandler));

        var run = await host.RunAsync(FunctionId, new InngestEvent(InngestJobEngine.EventName(Kind), new JobEvent("alpha")), Ct);

        run.Status.ShouldBe(TestRunStatus.Failed);
        run.FailureHandler.ShouldNotBeNull().Status.ShouldBe(TestRunStatus.Completed);
        var failure = recorder.Failures.ShouldHaveSingleItem();
        failure.Kind.ShouldBe(Kind);
        failure.Key.ShouldBe("alpha");
        failure.Attempts.ShouldBe(3);
        failure.Error.ShouldContain("Slack is down.");
    }

    [Fact]
    public async Task A_handler_without_a_failure_hook_gets_no_failure_function()
    {
        var recorder = new JobRecorder { Failure = new InvalidOperationException("Slack is down.") };
        await using var host = CreateTestHost(recorder);

        var run = await host.RunAsync(FunctionId, new InngestEvent(InngestJobEngine.EventName(Kind), new JobEvent("alpha")), Ct);

        run.Status.ShouldBe(TestRunStatus.Failed);
        run.FailureHandler.ShouldBeNull();
    }

    [Fact]
    public void The_inngest_provider_adds_the_sdks_health_check_as_degraded_only()
    {
        using var inngest = BuildHost(new Dictionary<string, string?> { ["INNGEST_DEV"] = "1" });
        var check = inngest.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(registration => registration.Name == JobsModule.InngestHealthCheckName).ShouldHaveSingleItem();
        check.FailureStatus.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public void The_in_process_engine_has_no_inngest_health_check()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Development });
        builder.AddJobs();
        builder.Services.AddHealthChecks();
        using var host = builder.Build();

        host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .ShouldNotContain(registration => registration.Name == JobsModule.InngestHealthCheckName);
    }

    [Fact]
    public void An_enabled_schedule_becomes_a_cron_function_that_runs_once_at_a_time()
    {
        var options = InngestJobEngine.CreateScheduleOptions(
            new EffectiveSchedule("refresh-web-sources", "ingestion.refresh-web", "all", "0 4 * * *", Enabled: true));

        options.Id.ShouldBe("schedule-refresh-web-sources");
        options.Name.ShouldBe("refresh-web-sources");
        options.Triggers.ShouldHaveSingleItem().ShouldBe(new CronTrigger("0 4 * * *"));
        options.Concurrency.ShouldBe([new ConcurrencyLimit(1)]);
    }

    [Fact]
    public async Task A_schedule_run_queues_its_job()
    {
        await using var host = CreateTestHost(new JobRecorder(), Schedule(enabled: true));

        var run = await host.RunAsync("schedule-nightly", new InngestEvent("inngest/scheduled.timer", new { cron = "0 4 * * *" }), Ct);

        run.Status.ShouldBe(TestRunStatus.Completed);
        var queued = run.SentEvents.ShouldHaveSingleItem();
        queued.Name.ShouldBe(InngestJobEngine.EventName(Kind));
        queued.Data.ShouldBe(new JobEvent("all"));
    }

    [Fact]
    public async Task Configuration_decides_whether_a_schedule_gets_its_function()
    {
        var enabled = new JobsOptions();
        enabled.Schedules["nightly"] = new JobScheduleOptions { Enabled = true };

        await using (var host = CreateTestHost(new JobRecorder(), Schedule(enabled: false)))
        {
            var exception = await Should.ThrowAsync<ArgumentException>(() => host.RunAsync("schedule-nightly", new InngestEvent("inngest/scheduled.timer", new { }), Ct));
            exception.Message.ShouldContain("schedule-nightly");
        }

        await using (var host = CreateTestHost(new JobRecorder(), Schedule(enabled: false), enabled))
        {
            (await host.RunAsync("schedule-nightly", new InngestEvent("inngest/scheduled.timer", new { }), Ct)).Status.ShouldBe(TestRunStatus.Completed);
        }
    }

    [Fact]
    public async Task A_job_runs_on_the_inngest_engine_with_its_key_and_attempt()
    {
        var recorder = new JobRecorder();
        await using var host = CreateTestHost(recorder);

        var run = await host.RunAsync(FunctionId, new InngestEvent(InngestJobEngine.EventName(Kind), new JobEvent("alpha")), Ct);

        run.Status.ShouldBe(TestRunStatus.Completed);
        var job = recorder.Jobs.ShouldHaveSingleItem();
        job.ShouldBe(new JobContext(run.RunId, Kind, "alpha", Attempt: 1, MaxAttempts: 3) { Steps = job.Steps });
        job.Steps.ShouldBeOfType<InngestJobSteps>();
    }

    [Fact]
    public async Task A_failing_job_is_retried_until_it_runs_out_of_attempts()
    {
        var recorder = new JobRecorder { Failure = new InvalidOperationException("The upstream service is down.") };
        await using var host = CreateTestHost(recorder);

        var run = await host.RunAsync(FunctionId, new InngestEvent(InngestJobEngine.EventName(Kind), new JobEvent("alpha")), Ct);

        run.Status.ShouldBe(TestRunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("The upstream service is down.");
        recorder.Jobs.Select(job => job.Attempt).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task An_event_without_a_job_key_fails_without_a_retry()
    {
        var recorder = new JobRecorder();
        await using var host = CreateTestHost(recorder);

        var run = await host.RunAsync(FunctionId, new InngestEvent(InngestJobEngine.EventName(Kind), new { }), Ct);

        run.Status.ShouldBe(TestRunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("no job key");
        recorder.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_queue_sends_one_event_per_job_and_dates_future_runs()
    {
        var client = new RecordingInngestClient();
        var queue = new InngestJobQueue(client, [new JobRegistration(Kind, typeof(WorkHandler), JobPolicy.Default)], new FakeTimeProvider(Start));

        await queue.EnqueueAsync(Kind, "now", cancellationToken: Ct);
        await queue.EnqueueAsync(Kind, "past", Start.AddMinutes(-1), Ct);
        await queue.EnqueueAsync(Kind, "later", Start.AddSeconds(30), Ct);

        client.Events.Select(@event => (@event.Name, ((JobEvent)@event.Data!).Key, @event.Timestamp)).ShouldBe(
        [
            ("jobs/test.work", "now", null),
            ("jobs/test.work", "past", null),
            ("jobs/test.work", "later", (DateTimeOffset?)Start.AddSeconds(30)),
        ]);
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => queue.EnqueueAsync("missing.kind", "x", cancellationToken: Ct));
        exception.Message.ShouldContain("missing.kind");
    }

    [Theory]
    [InlineData("https://jobs.example.com/inngest/", null, null, "https://jobs.example.com/inngest/")]
    [InlineData(null, "1", null, "http://localhost:8288")]
    [InlineData(null, "true", null, "http://localhost:8288")]
    [InlineData(null, "on", null, "http://localhost:8288")]
    [InlineData(null, "http://inngest:8288/", null, "http://inngest:8288")]
    [InlineData(null, "localhost:9288", null, "http://localhost:9288")]
    [InlineData(null, "0", "https://inngest.example.com/api", "https://inngest.example.com")]
    [InlineData(null, "Off", "https://inngest.example.com/api", "https://inngest.example.com")]
    [InlineData(null, "no", null, InngestJobEngine.CloudDashboardUrl)]
    [InlineData(null, null, null, InngestJobEngine.CloudDashboardUrl)]
    public void The_dashboard_link_follows_the_inngest_settings(string? configured, string? dev, string? baseUrl, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INNGEST_DEV"] = dev,
            ["INNGEST_BASE_URL"] = baseUrl,
        }).Build();

        InngestJobEngine.ResolveDashboardUrl(new InngestJobsOptions { DashboardUrl = configured }, configuration).ShouldBe(expected);
    }

    [Theory]
    [InlineData("http://localhost:8288", "01M66AE49V11B6EF9BKZ5D4M0S", "http://localhost:8288/run?runID=01M66AE49V11B6EF9BKZ5D4M0S")]
    [InlineData("https://jobs.example.com/inngest/", "01M66AE49V11B6EF9BKZ5D4M0S", "https://jobs.example.com/inngest/run?runID=01M66AE49V11B6EF9BKZ5D4M0S")]
    [InlineData("http://localhost:8288", null, null)]
    [InlineData(null, "01M66AE49V11B6EF9BKZ5D4M0S", null)]
    [InlineData(InngestJobEngine.CloudDashboardUrl, "01M66AE49V11B6EF9BKZ5D4M0S", null)]
    public void A_run_links_to_the_dashboard_except_on_inngest_cloud(string? dashboard, string? runId, string? expected) =>
        new JobEngineInfo("inngest", dashboard).RunUrl(runId).ShouldBe(expected);

    private static JobScheduleRegistration Schedule(bool enabled) => new("nightly", Kind, "all", "0 4 * * *", enabled);

    private static IHost BuildHost(Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:Provider"] = JobEngines.Inngest });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddSingleton(new JobRecorder());
        builder.AddJobs();
        builder.Services.AddJob<WorkHandler>(Kind, new JobPolicy { MaxAttempts = 3 });
        return builder.Build();
    }

    private static InngestTestHost CreateTestHost(JobRecorder recorder, JobScheduleRegistration? schedule = null, JobsOptions? settings = null, Type? handlerType = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(recorder);
        services.AddScoped<WorkHandler>();
        services.AddScoped<FailureAwareHandler>();
        var engine = new InngestJobEngine(services.AddInngest(options => options.AppId = "knowledge-agent"), settings ?? new JobsOptions());
        engine.AddFunction(new JobRegistration(Kind, handlerType ?? typeof(WorkHandler), new JobPolicy { MaxAttempts = 3 }));
        if (schedule is not null)
        {
            engine.AddSchedule(schedule);
        }

        return new InngestTestHost(services);
    }

    private sealed class JobRecorder
    {
        private readonly ConcurrentQueue<JobContext> _jobs = new();
        private readonly ConcurrentQueue<JobFailure> _failures = new();

        public Exception? Failure { get; init; }

        public IReadOnlyList<JobContext> Jobs => [.. _jobs];

        public IReadOnlyList<JobFailure> Failures => [.. _failures];

        public void Record(JobContext job) => _jobs.Enqueue(job);

        public void Record(JobFailure failure) => _failures.Enqueue(failure);
    }

    private sealed class FailureAwareHandler(JobRecorder recorder) : IJobHandler, IJobFailureHandler
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            recorder.Record(context);
            return recorder.Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
        {
            recorder.Record(failure);
            return Task.CompletedTask;
        }
    }

    private sealed class WorkHandler(JobRecorder recorder) : IJobHandler
    {
        public Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            recorder.Record(context);
            return recorder.Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }

    private sealed class RecordingInngestClient : IInngestClient
    {
        private readonly ConcurrentQueue<InngestEvent> _events = new();

        public string AppId => "knowledge-agent";

        public IReadOnlyList<InngestEvent> Events => [.. _events];

        public Task<SendEventsResult> SendAsync(InngestEvent @event, CancellationToken cancellationToken = default) =>
            SendAsync([@event], cancellationToken);

        public Task<SendEventsResult> SendAsync(IEnumerable<InngestEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var @event in events)
            {
                _events.Enqueue(@event);
            }

            return Task.FromResult(new SendEventsResult([]));
        }
    }
}
