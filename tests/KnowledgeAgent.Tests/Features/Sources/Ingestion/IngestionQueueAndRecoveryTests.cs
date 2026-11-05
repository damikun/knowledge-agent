using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Sources.Ingestion;

public sealed class IngestionQueueAndRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Queue_enqueues_an_ingestion_job_keyed_by_the_source()
    {
        var jobs = new RecordingJobQueue();
        var queue = new IngestionQueue(jobs, new FakeTimeProvider(KnowledgeTestContext.Start));
        var id = Guid.NewGuid();

        await queue.EnqueueAsync(id, Ct);
        await queue.EnqueueAsync(id, TimeSpan.FromSeconds(8), Ct);

        jobs.Enqueued.ShouldBe(
        [
            (IngestionJobs.IngestSource, id.ToString(), null),
            (IngestionJobs.IngestSource, id.ToString(), KnowledgeTestContext.Start.AddSeconds(8)),
        ]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(9, 512)]
    [InlineData(10, 600)]
    [InlineData(1000, 600)]
    public void Retry_delay_doubles_per_attempt_and_is_capped(int attempts, int expectedSeconds)
    {
        IngestionPipeline.RetryDelay(attempts).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public async Task Recovery_requeues_pending_and_processing_sources_oldest_first()
    {
        await using var context = new KnowledgeTestContext();
        var processing = await SeedAsync(context, SourceStatus.Processing);
        context.Time.Advance(TimeSpan.FromMinutes(1));
        await SeedAsync(context, SourceStatus.Ready);
        await SeedAsync(context, SourceStatus.Failed);
        var pending = await SeedAsync(context, SourceStatus.Pending);
        var initializer = context.Services.GetServices<IStartupInitializer>().OfType<IngestionRecoveryInitializer>().Single();

        await initializer.InitializeAsync(Ct);

        context.DrainQueue().ShouldBe([processing.Id, pending.Id]);
    }

    [Fact]
    public async Task Recovery_is_a_non_critical_ingestion_initializer()
    {
        await using var context = new KnowledgeTestContext();

        IStartupInitializer initializer = context.Services.GetServices<IStartupInitializer>().OfType<IngestionRecoveryInitializer>().Single();

        initializer.Order.ShouldBe(StartupOrder.Ingestion);
        initializer.IsCritical.ShouldBeFalse();
        initializer.Name.ShouldNotBeNullOrWhiteSpace();
    }

    private static Task<Source> SeedAsync(KnowledgeTestContext context, SourceStatus status)
    {
        var source = new Source { Type = SourceType.Text, Title = status.ToString(), Status = status };
        return context.SeedAsync(source, new SourceContent { SourceId = source.Id, ExtractedText = "text" });
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<(string Kind, string Key, DateTimeOffset? RunAt)> Enqueued { get; } = [];

        public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
        {
            Enqueued.Add((kind, key, runAt));
            return Task.CompletedTask;
        }
    }
}
