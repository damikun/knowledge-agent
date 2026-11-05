using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Sources.Ingestion;

internal sealed partial class IngestionRecoveryInitializer(
    IServiceScopeFactory scopeFactory,
    IIngestionQueue queue,
    WaitingSourcesQueuer waitingSources,
    ILogger<IngestionRecoveryInitializer> logger) : IStartupInitializer
{
    private static readonly SourceStatus[] UnfinishedStatuses = [SourceStatus.Pending, SourceStatus.Processing];

    public int Order => StartupOrder.Ingestion;

    public string Name => "Ingestion recovery";

    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISourceRepository>();
        var unfinished = await repository.ListByStatusAsync(UnfinishedStatuses, cancellationToken).ConfigureAwait(false);
        foreach (var source in unfinished.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id))
        {
            await queue.EnqueueAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }

        LogRecovered(logger, unfinished.Count);

        // A model may have been configured while the API was down.
        await waitingSources.QueueIfReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-enqueued {Count} unfinished sources for ingestion")]
    private static partial void LogRecovered(ILogger logger, int count);
}
