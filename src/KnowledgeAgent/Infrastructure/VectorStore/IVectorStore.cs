namespace KnowledgeAgent.Infrastructure.VectorStore;

public interface IVectorStore
{
    string ProviderName { get; }

    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of removed chunks, or -1 when the store cannot tell.</summary>
    Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns BM25 matches, or nothing when the store has no keyword support.</summary>
    Task<IReadOnlyList<VectorSearchHit>> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VectorSearchHit>>([]);

    /// <summary>One page of chunks containing the text, by source and chunk index; a chunk stored for several models counts once.</summary>
    Task<TextSearchPage<VectorSearchHit>> FindTextAsync(TextSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSearchPage<VectorSearchHit>.Empty);

    /// <summary>Returns one page of the sources whose chunks contain the text, the most matching chunks first.</summary>
    Task<TextSearchPage<SourceTextMatches>> CountTextBySourceAsync(TextSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSearchPage<SourceTextMatches>.Empty);

    /// <summary>Returns the chunks of a source ordered by chunk index.</summary>
    Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
