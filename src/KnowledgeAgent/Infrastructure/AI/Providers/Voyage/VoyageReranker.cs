namespace KnowledgeAgent.Infrastructure.AI.Providers.Voyage;

internal sealed class VoyageReranker(VoyageClient client, string model) : IReranker, IDisposable
{
    public Task<IReadOnlyList<double>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default) =>
        documents.Count == 0 ? Task.FromResult<IReadOnlyList<double>>([]) : client.RerankAsync(model, query, documents, cancellationToken);

    public void Dispose() => client.Dispose();
}
