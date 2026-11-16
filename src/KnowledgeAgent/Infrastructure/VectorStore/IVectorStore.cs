namespace KnowledgeAgent.Infrastructure.VectorStore;

public interface IVectorStore
{
    string ProviderName { get; }

    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default);

    Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VectorSearchHit>>([]);

    Task<TextSearchPage<VectorSearchHit>> FindTextAsync(TextSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSearchPage<VectorSearchHit>.Empty);

    Task<TextSearchPage<SourceTextMatches>> CountTextBySourceAsync(TextSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSearchPage<SourceTextMatches>.Empty);

    Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
