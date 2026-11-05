using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Sources.Ingestion;

// Queues the sources that wait for an embedding model as soon as one resolves.
internal sealed class WaitingSourcesQueuer(IEmbeddingService embeddings, IServiceScopeFactory scopeFactory) : IModelSettingsListener
{
    public Task OnModelSettingsChangedAsync(CancellationToken cancellationToken) => QueueIfReadyAsync(cancellationToken);

    public async Task<int> QueueIfReadyAsync(CancellationToken cancellationToken)
    {
        if (!await embeddings.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISourceService>().QueueWaitingAsync(cancellationToken).ConfigureAwait(false);
    }
}
