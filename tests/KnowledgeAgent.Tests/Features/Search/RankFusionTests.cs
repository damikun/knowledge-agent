using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Tests.Features.Search;

public sealed class RankFusionTests
{
    private static readonly Guid Source = Guid.NewGuid();

    [Fact]
    public void Chunks_both_retrievers_rank_high_come_first_with_relative_scores()
    {
        var both = Hit(0);
        var vectorOnly = Hit(1);
        var keywordOnly = Hit(2);

        var fused = RankFusion.Fuse([both, vectorOnly], [both, keywordOnly], k: 60, keywordRan: true);

        fused.Select(candidate => candidate.Chunk.ChunkId).ShouldBe([both.ChunkId, vectorOnly.ChunkId, keywordOnly.ChunkId]);
        fused[0].Score.ShouldBe(1, 1e-12);
        fused[0].VectorRank.ShouldBe(1);
        fused[0].KeywordRank.ShouldBe(1);
        fused[0].VectorScore.ShouldBe(both.Score);
        fused[1].Score.ShouldBe(61.0 / 62 / 2, 1e-12);
        fused[1].KeywordRank.ShouldBeNull();
        fused[2].VectorRank.ShouldBeNull();
        fused[2].KeywordScore.ShouldBe(keywordOnly.Score);
    }

    [Fact]
    public void Without_keyword_terms_the_vector_ranking_alone_scales_to_one()
    {
        var fused = RankFusion.Fuse([Hit(0), Hit(1)], [], k: 60, keywordRan: false);

        fused[0].Score.ShouldBe(1, 1e-12);
        fused[1].Score.ShouldBe(61.0 / 62, 1e-12);
    }

    [Fact]
    public void Weights_shift_the_balance_between_the_rankings()
    {
        var vectorFavourite = Hit(0);
        var keywordFavourite = Hit(1);

        var keywordHeavy = RankFusion.Fuse([vectorFavourite, keywordFavourite], [keywordFavourite, vectorFavourite], k: 60, keywordRan: true, vectorWeight: 0.25);
        var keywordOnly = RankFusion.Fuse([vectorFavourite, Hit(2)], [keywordFavourite], k: 60, keywordRan: true, vectorWeight: 0);

        keywordHeavy[0].Chunk.ChunkId.ShouldBe(keywordFavourite.ChunkId);
        keywordOnly[0].Chunk.ChunkId.ShouldBe(keywordFavourite.ChunkId);
        keywordOnly[0].Score.ShouldBe(1, 1e-12);
        keywordOnly.Skip(1).ShouldAllBe(candidate => candidate.Score == 0);
        keywordOnly[1].Chunk.ChunkId.ShouldBe(vectorFavourite.ChunkId, "vector hits still fill up the list, in their own order");
    }

    [Fact]
    public void Single_rankings_keep_cosine_scores_or_scale_bm25_to_the_best_hit()
    {
        var vector = RankFusion.Single([Hit(0, 0.8), Hit(1, 0.4)], keyword: false);
        var keyword = RankFusion.Single([Hit(0, 6), Hit(1, 3)], keyword: true);

        vector.Select(candidate => candidate.Score).ShouldBe([0.8, 0.4]);
        vector[1].VectorRank.ShouldBe(2);
        keyword.Select(candidate => candidate.Score).ShouldBe([1, 0.5]);
        keyword[0].KeywordScore.ShouldBe(6);
    }

    private static VectorSearchHit Hit(int index, double score = 0.5) =>
        new(Guid.NewGuid(), Source, index, $"chunk {index}", score, new Dictionary<string, string>());
}
