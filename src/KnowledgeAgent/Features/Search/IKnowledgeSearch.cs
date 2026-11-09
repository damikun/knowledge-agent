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

// Degraded lists the parts that failed and were skipped, so callers can tell a weaker search from an empty one.
public sealed record KnowledgeSearchResult(IReadOnlyList<KnowledgeSearchHit> Hits, IReadOnlyList<string> Degraded)
{
    public bool IsDegraded => Degraded.Count > 0;
}

public static class SearchDegradations
{
    public const string KeywordFailed = "keyword_failed";
    public const string RerankFailed = "rerank_failed";
    public const string SourceLookupFailed = "source_lookup_failed";
}

public interface IKnowledgeSearch
{
    Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken = default);
}
