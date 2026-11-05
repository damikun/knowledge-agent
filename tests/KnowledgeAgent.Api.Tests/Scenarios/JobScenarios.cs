using System.Diagnostics;
using System.Net;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Jobs;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class JobScenarios(AppFixture fixture)
{
    private const string FailingKind = "test.always-fails";
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Ingesting_a_source_runs_as_a_background_job_and_records_its_run()
    {
        var app = await fixture.GetAppAsync();

        var source = await app.AddReadyTextSourceAsync("Jobs handbook", "Background jobs run on Inngest, or inside the API in memory mode.");

        Guid.TryParse(source.JobRunId, out _).ShouldBeTrue();
        source.JobRunUrl.ShouldBeNull();
        source.Stage.ShouldBeNull();
        source.EmbeddedChunks.ShouldBe(source.ChunkCount);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Overview_names_the_engine_and_describes_the_schedules()
    {
        var app = await fixture.GetAppAsync();

        var overview = await app.Client.GetJsonAsync<JobsOverviewDto>("/api/jobs/overview");

        overview.Engine.ShouldBe("inprocess");
        overview.DashboardUrl.ShouldBeNull();
        overview.Schedules.Select(schedule => schedule.Name).ShouldBe(["refresh-web-sources", "widget-retention", "close-idle-conversations", "handover-mail-poll", "alerts-notify"], ignoreOrder: true);
        overview.Schedules.Single(schedule => schedule.Name == "refresh-web-sources")
            .ShouldBe(new JobScheduleDto("refresh-web-sources", "ingestion.refresh-web", "0 4 * * *", Enabled: false, NextRunAt: null));
        var poll = overview.Schedules.Single(schedule => schedule.Name == "handover-mail-poll");
        (poll.Kind, poll.Cron, poll.Enabled).ShouldBe(("handover.mail-poll", "* * * * *", true));
        var idle = overview.Schedules.Single(schedule => schedule.Name == "close-idle-conversations");
        (idle.Kind, idle.Cron, idle.Enabled).ShouldBe(("conversations.close-idle", "*/5 * * * *", true));
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Schedules_are_set_in_configuration()
    {
        await using var app = await fixture.CreateAppAsync(options =>
        {
            options.Settings["Jobs:Schedules:refresh-web-sources:Cron"] = "15 6 * * 1-5";
            options.Settings["Jobs:Schedules:refresh-web-sources:Enabled"] = "true";
        });

        var schedule = (await app.Client.GetJsonAsync<JobsOverviewDto>("/api/jobs/overview")).Schedules.Single(item => item.Name == "refresh-web-sources");

        schedule.Cron.ShouldBe("15 6 * * 1-5");
        schedule.Enabled.ShouldBeTrue();
        var next = schedule.NextRunAt.ShouldNotBeNull();
        next.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        next.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddDays(4));
        next.UtcDateTime.TimeOfDay.ShouldBe(new TimeSpan(6, 15, 0));
        next.UtcDateTime.DayOfWeek.ShouldNotBeOneOf(DayOfWeek.Saturday, DayOfWeek.Sunday);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Running_the_web_refresh_now_reingests_ready_web_sources()
    {
        await using var app = await fixture.CreateAppAsync();
        var web = await fixture.GetWebServerAsync();
        var path = $"/jobs/{ApiClient.UniqueWord()}.html";
        web.AddHtml(path, TestDocuments.HtmlArticle("Release Calendar", ["Releases ship on the first Monday of every month after the freeze."]));
        using var added = await app.Client.PostJsonAsync("/api/sources/url", new AddUrlSourceRequest(web.Url(path).ToString()));
        var source = await app.WaitForSourceAsync((await added.ReadJsonAsync<SourceDto>(HttpStatusCode.Accepted)).Id);
        var fetches = web.Hits(path);

        using var run = await app.Client.PostAsync("/api/jobs/schedules/refresh-web-sources/run", content: null, Ct);
        run.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await WaitUntilAsync(() => web.Hits(path) > fetches);
        var refreshed = await app.WaitForSourceAsync(source.Id);
        refreshed.Status.ShouldBe("ready");
        refreshed.JobRunId.ShouldNotBeNull().ShouldNotBe(source.JobRunId);

        using var missing = await app.Client.PostAsync("/api/jobs/schedules/no-such-schedule/run", content: null, Ct);
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_failing_job_is_retried_until_it_runs_out_of_attempts()
    {
        var handler = new AlwaysFailingJob();
        await using var app = await fixture.CreateAppAsync(options => options.ConfigureServices = services =>
        {
            services.AddSingleton(handler);
            services.AddJob<AlwaysFailingJob>(FailingKind, new JobPolicy { MaxAttempts = 2 });
        });

        await app.Services.GetRequiredService<IJobQueue>().EnqueueAsync(FailingKind, "nightly-export", cancellationToken: Ct);

        await WaitUntilAsync(() => app.Logs.Dump().Contains($"Job {FailingKind} nightly-export failed after 2 attempts", StringComparison.Ordinal));
        handler.Calls.ShouldBe(2);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > JobTimeout)
            {
                throw new ShouldAssertException("The jobs did not reach the expected state in time.");
            }

            await Task.Delay(100, Ct);
        }
    }

    private sealed class AlwaysFailingJob : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("The export target is offline.");
        }
    }
}

public sealed class InMemoryJobScenarios(InMemoryAppFixture fixture) : JobScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersJobScenarios(ContainersAppFixture fixture) : JobScenarios(fixture), IClassFixture<ContainersAppFixture>;
