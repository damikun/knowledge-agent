using System.Collections.Concurrent;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Sources.Support;

namespace KnowledgeAgent.Tests.Features.Search;

public sealed class HybridSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hybrid_search_fuses_both_rankings_and_reports_each_retriever()
    {
        await using var context = new KnowledgeTestContext();
        var manual = await SeedAsync(context, "Manual", "Error E-214 means an encoder mismatch on joint 2 of the arm.");
        await SeedAsync(context, "Cooking", "Slow cooked tomato sauce needs garlic and patience.");

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("what does E-214 mean", TopK: 2));

        hits[0].SourceId.ShouldBe(manual.Id);
        hits[0].KeywordRank.ShouldBe(1);
        hits[0].VectorRank.ShouldNotBeNull();
        hits[0].VectorScore.ShouldNotBeNull();
        hits[0].KeywordScore.ShouldNotBeNull();
        hits[0].Score.ShouldBeInRange(0, 1);
        hits[0].RerankScore.ShouldBeNull();
        var keyword = context.VectorStore.KeywordCalls.ShouldHaveSingleItem();
        keyword.Terms.ShouldBe(["e-214", "mean"]);
        keyword.EmbeddingModel.ShouldBe(context.Embeddings.ModelKey);
        keyword.TopK.ShouldBe(50);
    }

    [Fact]
    public async Task Vector_mode_skips_keyword_search_and_keyword_mode_ranks_by_matches()
    {
        await using var context = new KnowledgeTestContext();
        var first = await SeedAsync(context, "Laptops", "Pick up the laptop in room B2.17. The B2.17 desk opens at 8.");
        var second = await SeedAsync(context, "Rooms", "Room B2.17 is on the second floor.");

        var vector = await context.SearchAsync(new KnowledgeSearchRequest("room B2.17", Mode: SearchModes.Vector));
        context.VectorStore.KeywordCalls.ShouldBeEmpty();
        vector.ShouldAllBe(hit => hit.KeywordRank == null && hit.VectorRank != null);

        var keyword = await context.SearchAsync(new KnowledgeSearchRequest("room B2.17", Mode: "KEYWORD"));
        keyword.Select(hit => hit.SourceId).ShouldBe([first.Id, second.Id]);
        keyword[0].Score.ShouldBe(1);
        keyword.ShouldAllBe(hit => hit.VectorRank == null && hit.KeywordRank != null);
    }

    [Fact]
    public async Task The_configured_mode_applies_unless_the_request_names_one()
    {
        await using var context = new KnowledgeTestContext(configureSearch: options => options.Mode = SearchModes.Vector);
        await SeedAsync(context, "Doc", "configured mode content");

        await context.SearchAsync(new KnowledgeSearchRequest("configured mode content"));
        context.VectorStore.KeywordCalls.ShouldBeEmpty();

        await context.SearchAsync(new KnowledgeSearchRequest("configured mode content", Mode: SearchModes.Hybrid));
        context.VectorStore.KeywordCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Source_filters_reach_both_retrievers_and_min_score_only_the_vector_one()
    {
        await using var context = new KnowledgeTestContext();
        var relevant = await SeedAsync(context, "Relevant", "kubernetes cluster autoscaling with node pools");
        var weak = await SeedAsync(context, "Weak", "banana bread for the kubernetes team party");

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("kubernetes cluster autoscaling", SourceIds: [relevant.Id, weak.Id], MinScore: 0.99));

        context.VectorStore.KeywordCalls.ShouldHaveSingleItem().SourceIds!.ShouldBe([relevant.Id, weak.Id], ignoreOrder: true);
        hits.Select(hit => hit.SourceId).ShouldContain(weak.Id, "keyword candidates are not cut by the cosine threshold");
        hits.Single(hit => hit.SourceId == weak.Id).VectorRank.ShouldBeNull();
    }

    [Fact]
    public async Task A_failing_keyword_search_falls_back_to_vector_results()
    {
        await using var context = new KnowledgeTestContext();
        var source = await SeedAsync(context, "Doc", "fallback content for the vector search");
        context.VectorStore.KeywordFailure = new InvalidOperationException("keyword index down");

        var hit = (await context.SearchAsync(new KnowledgeSearchRequest("fallback content"))).ShouldHaveSingleItem();

        hit.SourceId.ShouldBe(source.Id);
        hit.KeywordRank.ShouldBeNull();
    }

    [Fact]
    public async Task The_reranker_reorders_the_candidates_and_its_relevance_becomes_the_score()
    {
        var reranker = new FakeReranker(documents => documents.Select(document => document.Contains("garlic", StringComparison.Ordinal) ? 0.9 : 0.2).ToList());
        await using var context = new KnowledgeTestContext(reranker: reranker);
        var sauce = await SeedAsync(context, "Sauce", "Tomato sauce needs garlic.");
        await SeedAsync(context, "Soup", "Tomato soup is served hot, tomato after tomato.");

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("tomato", TopK: 2));

        hits[0].SourceId.ShouldBe(sauce.Id);
        hits[0].Score.ShouldBe(0.9);
        hits[0].RerankScore.ShouldBe(0.9);
        hits[1].RerankScore.ShouldBe(0.2);
        var call = reranker.Calls.ShouldHaveSingleItem();
        call.Query.ShouldBe("tomato");
        call.Documents.ShouldContain(document => document.StartsWith("Sauce\n\nTomato sauce needs garlic.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reranking_can_be_switched_off_and_never_breaks_search()
    {
        var reranker = new FakeReranker(_ => throw new HttpRequestException("reranker down"));
        await using var context = new KnowledgeTestContext(reranker: reranker);
        await SeedAsync(context, "One", "resilient ranking content one");
        await SeedAsync(context, "Two", "resilient ranking content two");

        var failed = await context.SearchAsync(new KnowledgeSearchRequest("resilient ranking content"));
        var skipped = await context.SearchAsync(new KnowledgeSearchRequest("resilient ranking content", Rerank: false));

        failed.Count.ShouldBe(2);
        failed.ShouldAllBe(hit => hit.RerankScore == null);
        skipped.Count.ShouldBe(2);
        reranker.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_configured_reranker_the_fused_order_stays()
    {
        var reranker = new FakeReranker(_ => null);
        await using var context = new KnowledgeTestContext(reranker: reranker);
        await SeedAsync(context, "One", "unreranked content alpha");
        await SeedAsync(context, "Two", "unreranked content beta");

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("unreranked content"));

        hits.ShouldAllBe(hit => hit.RerankScore == null);
        reranker.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Hits_of_deleted_sources_never_reach_the_reranker()
    {
        var reranker = new FakeReranker(documents => documents.Select(_ => 0.5).ToList());
        await using var context = new KnowledgeTestContext(reranker: reranker);
        await SeedAsync(context, "Kept", "orphan check content that stays");
        await context.VectorStore.InsertAsync(
            [new ChunkRecord(Guid.NewGuid(), Guid.NewGuid(), 0, "orphan check content that goes", context.Embeddings.ModelKey,
                FakeEmbeddingGenerator.Embed("orphan check content that goes", context.Embeddings.Dimensions),
                new Dictionary<string, string> { [ChunkMetadataKeys.Title] = "Ghost" })],
            Ct);

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("orphan check content"));

        hits.ShouldHaveSingleItem().SourceTitle.ShouldBe("Kept");
        reranker.Calls.ShouldBeEmpty("a single live candidate needs no reranking");
    }

    private static async Task<Source> SeedAsync(KnowledgeTestContext context, string title, string text)
    {
        var source = await context.SeedTextAsync(title, text);
        await context.ProcessAsync(source.Id, Ct);
        return source;
    }

    private sealed record RerankCall(string Query, IReadOnlyList<string> Documents);

    private sealed class FakeReranker(Func<IReadOnlyList<string>, IReadOnlyList<double>?> score) : IRerankService
    {
        public ConcurrentQueue<RerankCall> Calls { get; } = new();

        public Task<string?> DescribeAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("fake/reranker");

        public Task<RerankResult?> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue(new RerankCall(query, documents));
            var scores = score(documents);
            return Task.FromResult(scores is null ? null : new RerankResult("fake/reranker", scores));
        }
    }
}
