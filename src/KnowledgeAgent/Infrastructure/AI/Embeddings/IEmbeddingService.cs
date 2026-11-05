namespace KnowledgeAgent.Infrastructure.AI.Embeddings;

public enum EmbeddingPurpose
{
    Document,
    Query,
}

public sealed record EmbeddingResult(string ModelKey, int Dimensions, IReadOnlyList<ReadOnlyMemory<float>> Vectors);

public interface IEmbeddingService
{
    /// <summary>Whether the embedding profile resolves to a model; embeds nothing.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default);
}
