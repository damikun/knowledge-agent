namespace KnowledgeAgent.Features.Search;

internal static class SearchMappings
{
    public static SearchHitDto ToDto(this KnowledgeSearchHit hit) => new(
        hit.ChunkId,
        hit.SourceId,
        hit.ChunkIndex,
        hit.SourceTitle,
        hit.SourceUrl,
        hit.Page,
        hit.Content,
        hit.Score,
        hit.VectorScore,
        hit.VectorRank,
        hit.KeywordScore,
        hit.KeywordRank,
        hit.RerankScore);
}
