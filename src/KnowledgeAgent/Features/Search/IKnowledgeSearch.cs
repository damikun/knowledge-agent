namespace KnowledgeAgent.Features.Search;

public sealed record KnowledgeSearchRequest(
    string Query,
    int TopK = 6,
    IReadOnlyCollection<Guid>? SourceIds = null,
    double? MinScore = null,
    string? Mode = null,
    bool? Rerank = null);

public sealed record KnowledgeSearchHit(
    Guid ChunkId,
    Guid SourceId,
    int ChunkIndex,
    string SourceTitle,
    string? SourceUrl,
    int? Page,
    string Content,
    double Score)
{
    public double? VectorScore { get; init; }
    public int? VectorRank { get; init; }
    public double? KeywordScore { get; init; }
    public int? KeywordRank { get; init; }
    public double? RerankScore { get; init; }
}

public interface IKnowledgeSearch
{
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken = default);
}
