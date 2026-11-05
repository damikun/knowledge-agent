using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Features.Search;

internal sealed record FusedCandidate(
    VectorSearchHit Chunk,
    double Score,
    double? VectorScore,
    int? VectorRank,
    double? KeywordScore,
    int? KeywordRank)
{
    public int BestRank => Math.Min(VectorRank ?? int.MaxValue, KeywordRank ?? int.MaxValue);
}

internal static class RankFusion
{
    public static IReadOnlyList<FusedCandidate> Fuse(
        IReadOnlyList<VectorSearchHit> vector,
        IReadOnlyList<VectorSearchHit> keyword,
        int k,
        bool keywordRan,
        double vectorWeight = 1,
        double keywordWeight = 1)
    {
        var vectorShare = Math.Max(0, vectorWeight);
        var keywordShare = keywordRan ? Math.Max(0, keywordWeight) : 0;
        var rrfK = Math.Max(1, k);
        var byChunk = new Dictionary<Guid, (VectorSearchHit Chunk, double? VectorScore, int? VectorRank, double? KeywordScore, int? KeywordRank)>();
        for (var i = 0; i < vector.Count; i++)
        {
            byChunk.TryAdd(vector[i].ChunkId, (vector[i], vector[i].Score, i + 1, null, null));
        }

        for (var i = 0; i < keyword.Count; i++)
        {
            byChunk[keyword[i].ChunkId] = byChunk.TryGetValue(keyword[i].ChunkId, out var found)
                ? found with { KeywordScore = keyword[i].Score, KeywordRank = i + 1 }
                : (keyword[i], null, null, keyword[i].Score, i + 1);
        }

        // Relative to a chunk that every retriever ranked first, so scores stay in 0..1.
        var maximum = (vectorShare + keywordShare) / (rrfK + 1);
        return byChunk.Values
            .Select(entry => new FusedCandidate(
                entry.Chunk,
                maximum > 0 ? (vectorShare * Reciprocal(entry.VectorRank, rrfK) + keywordShare * Reciprocal(entry.KeywordRank, rrfK)) / maximum : 0,
                entry.VectorScore,
                entry.VectorRank,
                entry.KeywordScore,
                entry.KeywordRank))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.BestRank)
            .ThenBy(candidate => candidate.Chunk.SourceId)
            .ThenBy(candidate => candidate.Chunk.ChunkIndex)
            .ToList();
    }

    public static IReadOnlyList<FusedCandidate> Single(IReadOnlyList<VectorSearchHit> hits, bool keyword)
    {
        // BM25 has no fixed scale: report it relative to the best hit so scores stay in 0..1 like the other modes.
        var best = keyword && hits.Count > 0 ? hits.Max(hit => hit.Score) : 1d;
        return hits
            .Select((hit, index) => keyword
                ? new FusedCandidate(hit, best > 0 ? hit.Score / best : 0, null, null, hit.Score, index + 1)
                : new FusedCandidate(hit, hit.Score, hit.Score, index + 1, null, null))
            .ToList();
    }

    private static double Reciprocal(int? rank, int k) => rank is { } value ? 1d / (k + value) : 0d;
}
