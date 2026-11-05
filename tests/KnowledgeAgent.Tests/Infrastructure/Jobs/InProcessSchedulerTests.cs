using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Infrastructure.Jobs;

public sealed class InProcessSchedulerTests
{
    private const string Name = "nightly-report";
    private const string Kind = "test.report";
    private const string Cron = "30 4 * * *";
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstOccurrence = new(2026, 10, 29, 4, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_tick_waits_for_the_next_occurrence()
    {
        using var host = SchedulerHost.Create();

        (await host.TickAsync()).ShouldBe(0);

        host.Queue.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Due_occurrence_queues_one_job_and_moves_to_the_next_occurrence()
    {
        using var host = SchedulerHost.Create();
        await host.TickAsync();

        host.Time.SetUtcNow(FirstOccurrence.AddSeconds(-1));
        (await host.TickAsync()).ShouldBe(0);
        host.Time.SetUtcNow(FirstOccurrence);
        (await host.TickAsync()).ShouldBe(1);
        (await host.TickAsync()).ShouldBe(0);
        host.Time.SetUtcNow(FirstOccurrence.AddDays(1));
        (await host.TickAsync()).ShouldBe(1);

        host.Queue.Jobs.ShouldBe([(Kind, "all"), (Kind, "all")]);
    }

    [Fact]
    public async Task Several_missed_occurrences_queue_a_single_job()
    {
        using var host = SchedulerHost.Create();
        await host.TickAsync();

        host.Time.SetUtcNow(FirstOccurrence.AddDays(3));

        (await host.TickAsync()).ShouldBe(1);
        (await host.TickAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Disabled_schedule_queues_nothing()
    {
        using var host = SchedulerHost.Create(enabled: false);
        await host.TickAsync();

        host.Time.SetUtcNow(FirstOccurrence);

        (await host.TickAsync()).ShouldBe(0);
        host.Queue.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Configuration_enables_the_schedule_and_changes_its_cron()
    {
        using var host = SchedulerHost.Create(enabled: false, new Dictionary<string, string?>
        {
            [$"Jobs:Schedules:{Name}:Cron"] = "0 12 * * *",
            [$"Jobs:Schedules:{Name}:Enabled"] = "true",
        });
        await host.TickAsync();

        host.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        (await host.TickAsync()).ShouldBe(1);
    }

    [Fact]
    public void Configuration_beats_the_code_default()
    {
        var registration = new JobScheduleRegistration(Name, Kind, "all", Cron, Enabled: false);
        var options = new JobsOptions();
        options.Schedules["NIGHTLY-REPORT"] = new JobScheduleOptions { Cron = "0 4 * * *" };

        JobSchedules.Resolve(registration, new JobsOptions()).ShouldBe(new EffectiveSchedule(Name, Kind, "all", Cron, Enabled: false));
        JobSchedules.Resolve(registration, options).ShouldBe(new EffectiveSchedule(Name, Kind, "all", "0 4 * * *", Enabled: false));

        options.Schedules[Name].Enabled = true;
        JobSchedules.Resolve(registration, options).ShouldBe(new EffectiveSchedule(Name, Kind, "all", "0 4 * * *", Enabled: true));
    }

    [Theory]
    [InlineData("0 4 * * *", true)]
    [InlineData("*/15 8-18 * * 1-5", true)]
    [InlineData("0 4 * *", false)]
    [InlineData("61 * * * *", false)]
    [InlineData("0 0 4 * * *", false)]
    public void Cron_expressions_have_five_fields(string cron, bool valid) => JobSchedules.IsValidCron(cron).ShouldBe(valid);

    [Fact]
    public void Occurrences_are_evaluated_in_utc()
    {
        JobSchedules.NextOccurrence(Cron, Start).ShouldBe(FirstOccurrence);
        JobSchedules.NextOccurrence(Cron, FirstOccurrence).ShouldBe(FirstOccurrence.AddDays(1));
    }

    private sealed class SchedulerHost : IDisposable
    {
        private readonly IHost _host;

        private SchedulerHost(IHost host, FakeTimeProvider time, RecordingQueue queue)
        {
            _host = host;
            Time = time;
            Queue = queue;
        }

        public FakeTimeProvider Time { get; }

        public RecordingQueue Queue { get; }

        public static SchedulerHost Create(bool enabled = true, Dictionary<string, string?>? settings = null)
        {
            var time = new FakeTimeProvider(Start);
            var queue = new RecordingQueue();
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
            });
            builder.Configuration.AddInMemoryCollection(settings ?? []);
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.AddJobs();
            builder.Services.Replace(ServiceDescriptor.Singleton<IJobQueue>(queue));
            builder.Services.AddJobSchedule(new JobScheduleRegistration(Name, Kind, "all", Cron, enabled));
            return new SchedulerHost(builder.Build(), time, queue);
        }

        public Task<int> TickAsync() =>
            _host.Services.GetServices<IHostedService>().OfType<InProcessScheduler>().Single().TickAsync(Ct);

        public void Dispose() => _host.Dispose();
    }

    private sealed class RecordingQueue : IJobQueue
    {
        private readonly ConcurrentQueue<(string Kind, string Key)> _jobs = new();

        public IReadOnlyList<(string Kind, string Key)> Jobs => [.. _jobs];

        public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
        {
            _jobs.Enqueue((kind, key));
            return Task.CompletedTask;
        }
    }
}
