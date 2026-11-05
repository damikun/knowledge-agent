namespace KnowledgeAgent.Infrastructure.VectorStore;

internal static class VectorStoreLimits
{
    public const int MaxTopK = 100;
    public const int MaxPageSize = 1000;

    // Embedding dimensions are stored as UInt16.
    public const int MaxDimensions = ushort.MaxValue;
}
