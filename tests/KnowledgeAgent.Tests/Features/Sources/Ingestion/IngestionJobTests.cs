using System.Collections.Concurrent;
using System.Globalization;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Sources.Ingestion;

public sealed class IngestionJobTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Source_job_runs_the_pipeline_for_the_source_in_its_key()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Queued", TestDocuments.Prose(2));

        await context.WithScopeAsync(async services =>
        {
            var job = services.GetRequiredService<IngestSourceJob>();
            await job.HandleAsync(new JobContext("job-1", IngestionJobs.IngestSource, "not-a-source-id", 1, 3), Ct);
            await job.HandleAsync(new JobContext("job-1", IngestionJobs.IngestSource, source.Id.ToString(), 1, 3), Ct);
        });

        var processed = context.Repository.Find(source.Id)!;
        processed.Status.ShouldBe(SourceStatus.Ready);
        processed.JobRunId.ShouldBe("job-1");
    }

    [Fact]
    public async Task A_source_job_that_fails_for_good_marks_an_unfinished_source_failed()
    {
        await using var context = new KnowledgeTestContext();
        var processing = await SeedAsync(context, SourceType.Text, SourceStatus.Processing, 0);
        var ready = await SeedAsync(context, SourceType.Text, SourceStatus.Ready, 1);

        await context.WithScopeAsync(async services =>
        {
            var job = services.GetRequiredService<IngestSourceJob>();
            await job.OnFailureAsync(new JobFailure("job-1", IngestionJobs.IngestSource, processing.Id.ToString(), 3, "The database is unreachable."), Ct);
            await job.OnFailureAsync(new JobFailure("job-2", IngestionJobs.IngestSource, ready.Id.ToString(), 3, "The database is unreachable."), Ct);
        });

        var failed = context.Repository.Find(processing.Id)!;
        failed.Status.ShouldBe(SourceStatus.Failed);
        failed.Stage.ShouldBeNull();
        failed.Error.ShouldBe("The database is unreachable.");
        context.Repository.Find(ready.Id)!.Status.ShouldBe(SourceStatus.Ready);
        context.Repository.Find(ready.Id)!.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_job_reingests_every_ready_web_source()
    {
        await using var context = new KnowledgeTestContext();
        var ready = new List<Guid>();
        for (var i = 0; i < SourceQuery.MaxLimit + 5; i++)
        {
            ready.Add((await SeedAsync(context, SourceType.Url, SourceStatus.Ready, i)).Id);
        }

        var failed = await SeedAsync(context, SourceType.Url, SourceStatus.Failed, 0);
        var text = await SeedAsync(context, SourceType.Text, SourceStatus.Ready, 0);

        await context.WithScopeAsync(services => services.GetRequiredService<RefreshWebSourcesJob>()
            .HandleAsync(new JobContext("job-1", IngestionJobs.RefreshWebSources, "all", 1, 5), Ct));

        context.DrainQueue().ShouldBe(ready, ignoreOrder: true);
        ready.ShouldAllBe(id => context.Repository.Find(id)!.Status == SourceStatus.Pending);
        context.Repository.Find(failed.Id)!.Status.ShouldBe(SourceStatus.Failed);
        context.Repository.Find(text.Id)!.Status.ShouldBe(SourceStatus.Ready);
    }

    [Fact]
    public async Task Queued_sources_run_in_parallel_up_to_the_configured_parallelism()
    {
        var probe = new PipelineProbe();
        var allBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Behavior = async (_, _) =>
        {
            // The first three sources only finish once three run at the same time.
            if (probe.CurrentConcurrency >= 3)
            {
                allBusy.TrySetResult();
            }

            await allBusy.Task.WaitAsync(Timeout);
        };
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 3);
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            await host.EnqueueAsync(id);
        }

        await probe.WaitForProcessedAsync(ids.Count);
        probe.Processed.ShouldBe(ids, ignoreOrder: true);
        probe.MaxConcurrency.ShouldBe(3);
    }

    [Fact]
    public async Task Every_job_gets_its_own_scope()
    {
        var probe = new PipelineProbe();
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 2);

        for (var i = 0; i < 5; i++)
        {
            await host.EnqueueAsync(Guid.NewGuid());
        }

        await probe.WaitForProcessedAsync(5);
        probe.PipelineInstances.Distinct().Count().ShouldBe(5);
        await WaitUntilAsync(() => probe.DisposedScopes == 5);
    }

    [Fact]
    public async Task A_failing_job_is_retried_in_the_same_run()
    {
        var failing = Guid.NewGuid();
        var failures = 0;
        var probe = new PipelineProbe
        {
            Behavior = (id, _) => id == failing && Interlocked.Increment(ref failures) == 1
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask,
        };
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 1);
        var healthy = Guid.NewGuid();

        await host.EnqueueAsync(failing);
        await host.EnqueueAsync(healthy);

        await probe.WaitForProcessedAsync(2);
        probe.Processed.ShouldBe([failing, healthy]);
        probe.Failures.ShouldBe([failing]);
        var runs = probe.Runs.Where(run => run.SourceId == failing).Select(run => run.JobRunId).ToList();
        runs.Count.ShouldBe(2);
        runs.ShouldAllBe(run => run == runs[0]);
        runs[0].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_source_queued_while_it_runs_runs_once_more_afterwards_and_never_concurrently()
    {
        var id = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var probe = new PipelineProbe();
        probe.Behavior = async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(Timeout);
            }
        };
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 3);

        await host.EnqueueAsync(id);
        await started.Task.WaitAsync(Timeout, Ct);
        await host.EnqueueAsync(id);
        await host.EnqueueAsync(id);

        await Task.Delay(100, Ct);
        probe.Processed.ShouldBeEmpty();
        release.SetResult();

        await probe.WaitForProcessedAsync(2);
        await Task.Delay(100, Ct);
        probe.Processed.ShouldBe([id, id]);
        probe.MaxConcurrency.ShouldBe(1);
    }

    [Fact]
    public async Task A_burst_of_requests_for_a_source_runs_it_once_after_the_debounce_period()
    {
        var probe = new PipelineProbe();
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 3);
        var id = Guid.NewGuid();

        await host.Queue.EnqueueAsync(id, Ct);
        host.Time.Advance(IngestionJobs.Debounce / 2);
        await host.Queue.EnqueueAsync(id, Ct);
        host.Time.Advance(IngestionJobs.Debounce / 2);
        await Task.Delay(100, Ct);
        probe.Processed.ShouldBeEmpty();

        host.Time.Advance(IngestionJobs.Debounce / 2);

        await probe.WaitForProcessedAsync(1);
        await Task.Delay(100, Ct);
        probe.Processed.ShouldBe([id]);
    }

    [Fact]
    public async Task Stopping_the_host_cancels_running_work_without_a_retry()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new PipelineProbe();
        probe.Behavior = async (_, ct) =>
        {
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        };
        await using var host = await IngestionHost.StartAsync(probe, maxParallelism: 1);
        var id = Guid.NewGuid();
        await host.EnqueueAsync(id);
        await WaitUntilAsync(() => probe.CurrentConcurrency == 1);

        await host.StopAsync();

        await cancelled.Task.WaitAsync(Timeout, Ct);
        probe.Runs.ShouldHaveSingleItem();
        probe.Failures.ShouldBeEmpty();
    }

    private static Task<Source> SeedAsync(KnowledgeTestContext context, SourceType type, SourceStatus status, int index)
    {
        var source = new Source
        {
            Type = type,
            Status = status,
            Title = $"{type} {status} {index}",
            Url = type == SourceType.Url ? $"https://example.com/{status}/{index}" : null,
        };
        return context.SeedAsync(source, new SourceContent { SourceId = source.Id, ExtractedText = "text" });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class IngestionHost : IAsyncDisposable
    {
        private readonly IHost _host;
        private bool _stopped;

        private IngestionHost(IHost host, FakeTimeProvider time)
        {
            _host = host;
            Time = time;
        }

        public IIngestionQueue Queue => _host.Services.GetRequiredService<IIngestionQueue>();

        public FakeTimeProvider Time { get; }

        // Queues the source and lets its debounce period pass, so it runs.
        public async Task EnqueueAsync(Guid sourceId)
        {
            await Queue.EnqueueAsync(sourceId, TestContext.Current.CancellationToken);
            Time.Advance(IngestionJobs.Debounce);
        }

        public static async Task<IngestionHost> StartAsync(PipelineProbe probe, int maxParallelism)
        {
            var time = new FakeTimeProvider(KnowledgeTestContext.Start);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = PersistenceModule.InMemoryProvider,
                ["Persistence:InMemoryDatabaseName"] = $"ingestion-jobs-{Guid.NewGuid():N}",
                ["Ingestion:MaxParallelism"] = maxParallelism.ToString(CultureInfo.InvariantCulture),
            });
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.AddPersistence();
            builder.AddJobs();
            builder.AddSources();
            builder.Services.AddSingleton(probe);
            builder.Services.Replace(ServiceDescriptor.Scoped<IIngestionPipeline, RecordingPipeline>());

            var host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
            return new IngestionHost(host, time);
        }

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

    private sealed class PipelineProbe
    {
        private int _current;
        private int _max;
        private int _disposedScopes;

        public Func<Guid, CancellationToken, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

        public ConcurrentQueue<Guid> ProcessedQueue { get; } = new();

        public ConcurrentQueue<Guid> FailureQueue { get; } = new();

        public ConcurrentQueue<Guid> PipelineInstances { get; } = new();

        public ConcurrentQueue<(Guid SourceId, string? JobRunId)> RunQueue { get; } = new();

        public IReadOnlyList<(Guid SourceId, string? JobRunId)> Runs => [.. RunQueue];

        public IReadOnlyList<Guid> Processed => [.. ProcessedQueue];

        public IReadOnlyList<Guid> Failures => [.. FailureQueue];

        public int CurrentConcurrency => Volatile.Read(ref _current);

        public int MaxConcurrency => Volatile.Read(ref _max);

        public int DisposedScopes => Volatile.Read(ref _disposedScopes);

        public async Task RunAsync(Guid instanceId, Guid sourceId, string? jobRunId, CancellationToken cancellationToken)
        {
            PipelineInstances.Enqueue(instanceId);
            RunQueue.Enqueue((sourceId, jobRunId));
            var current = Interlocked.Increment(ref _current);
            int max;
            while (current > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, current, max) != max)
            {
            }

            try
            {
                await Behavior(sourceId, cancellationToken);
                ProcessedQueue.Enqueue(sourceId);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                FailureQueue.Enqueue(sourceId);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public void ScopeDisposed() => Interlocked.Increment(ref _disposedScopes);

        public async Task WaitForProcessedAsync(int count)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (ProcessedQueue.Count < count)
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private sealed class RecordingPipeline(PipelineProbe probe) : IIngestionPipeline, IDisposable
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        public Task ProcessAsync(Guid sourceId, string? jobRunId, CancellationToken cancellationToken = default) =>
            probe.RunAsync(_instanceId, sourceId, jobRunId, cancellationToken);

        public void Dispose() => probe.ScopeDisposed();
    }
}
