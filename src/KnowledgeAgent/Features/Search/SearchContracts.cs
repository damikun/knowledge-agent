namespace KnowledgeAgent.Features.Search;

public sealed record SearchRequest(
    string Query,
    int? TopK = null,
    IReadOnlyList<Guid>? SourceIds = null,
    double? MinScore = null,
    string? Mode = null,
    bool? Rerank = null);

public sealed record SearchHitDto(
    Guid ChunkId,
    Guid SourceId,
    int ChunkIndex,
    string SourceTitle,
    string? SourceUrl,
    int? Page,
    string Content,
    double Score,
    double? VectorScore = null,
    int? VectorRank = null,
    double? KeywordScore = null,
    int? KeywordRank = null,
    double? RerankScore = null);
