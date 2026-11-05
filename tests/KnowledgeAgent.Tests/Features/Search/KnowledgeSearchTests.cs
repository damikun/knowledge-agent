using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Sources.Support;

namespace KnowledgeAgent.Tests.Features.Search;

public sealed class KnowledgeSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Most_relevant_chunks_rank_first()
    {
        await using var context = new KnowledgeTestContext();
        var vectors = await context.SeedTextAsync("Vector databases", "Vector databases store embeddings and answer cosine similarity queries over vectors.");
        var cooking = await context.SeedTextAsync("Cooking", "Slow cooked tomato sauce needs garlic, olive oil and patience.");
        var hiking = await context.SeedTextAsync("Hiking", "Mountain trails require sturdy boots, water and a reliable map.");
        foreach (var id in new[] { vectors.Id, cooking.Id, hiking.Id })
        {
            await context.ProcessAsync(id, Ct);
        }

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("cosine similarity of vector embeddings", TopK: 3));

        hits.Count.ShouldBe(3);
        hits[0].SourceId.ShouldBe(vectors.Id);
        hits[0].SourceTitle.ShouldBe("Vector databases");
        hits[0].Content.ShouldContain("cosine similarity");
        hits.Select(h => h.Score).ShouldBeInOrder(SortDirection.Descending);
        hits[0].Score.ShouldBeGreaterThan(hits[1].Score);
    }

    [Fact]
    public async Task Query_is_embedded_with_the_query_purpose()
    {
        await using var context = new KnowledgeTestContext();

        await context.SearchAsync(new KnowledgeSearchRequest("  what is retrieval?  "));

        var call = context.Embeddings.Calls.ShouldHaveSingleItem();
        call.Purpose.ShouldBe(EmbeddingPurpose.Query);
        call.Inputs.ShouldBe(["what is retrieval?"]);
    }

    [Fact]
    public async Task Title_url_and_page_come_from_chunk_metadata()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedAsync(new Source { Type = SourceType.Url, Title = "Stored title", Url = "https://stored.example.com/" });
        await InsertChunkAsync(context, source.Id, 4, "paged knowledge content", new Dictionary<string, string>
        {
            [ChunkMetadataKeys.Title] = "Chunk title",
            [ChunkMetadataKeys.Url] = "https://chunk.example.com/doc",
            [ChunkMetadataKeys.Page] = "7",
        });

        var hit = (await context.SearchAsync(new KnowledgeSearchRequest("paged knowledge content"))).ShouldHaveSingleItem();

        hit.SourceId.ShouldBe(source.Id);
        hit.ChunkIndex.ShouldBe(4);
        hit.SourceTitle.ShouldBe("Chunk title");
        hit.SourceUrl.ShouldBe("https://chunk.example.com/doc");
        hit.Page.ShouldBe(7);
        hit.Content.ShouldBe("paged knowledge content");
        hit.Score.ShouldBe(1d, tolerance: 1e-6);
    }

    [Fact]
    public async Task Missing_metadata_falls_back_to_the_source_record()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedAsync(new Source { Type = SourceType.Url, Title = "Repository title", Url = "https://repo.example.com/page" });
        await InsertChunkAsync(context, source.Id, 0, "content without metadata", new Dictionary<string, string>());

        var hit = (await context.SearchAsync(new KnowledgeSearchRequest("content without metadata"))).ShouldHaveSingleItem();

        hit.SourceTitle.ShouldBe("Repository title");
        hit.SourceUrl.ShouldBe("https://repo.example.com/page");
        hit.Page.ShouldBeNull();
    }

    [Fact]
    public async Task Only_chunks_of_the_active_embedding_model_are_searched()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Model test", "shared words about embeddings");
        await context.ProcessAsync(source.Id, Ct);
        await InsertChunkAsync(context, source.Id, 99, "shared words about embeddings", new Dictionary<string, string>(), modelKey: "fake:old-model:64");

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("shared words about embeddings", TopK: 10));

        hits.ShouldHaveSingleItem().ChunkIndex.ShouldBe(0);
        context.VectorStore.All.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    [InlineData(500, 50)]
    public async Task TopK_is_clamped_between_1_and_50(int requested, int expected)
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedAsync(new Source { Type = SourceType.Text, Title = "Many chunks" });
        for (var i = 0; i < 60; i++)
        {
            await InsertChunkAsync(context, source.Id, i, $"chunk number {i} about clamping", new Dictionary<string, string>());
        }

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("clamping", TopK: requested));

        hits.Count.ShouldBe(expected);
    }

    [Fact]
    public async Task Source_filter_restricts_the_hits()
    {
        await using var context = new KnowledgeTestContext();
        var first = await context.SeedTextAsync("First", "common topic described in the first source");
        var second = await context.SeedTextAsync("Second", "common topic described in the second source");
        await context.ProcessAsync(first.Id, Ct);
        await context.ProcessAsync(second.Id, Ct);

        var filtered = await context.SearchAsync(new KnowledgeSearchRequest("common topic", SourceIds: [second.Id]));
        var unfiltered = await context.SearchAsync(new KnowledgeSearchRequest("common topic", SourceIds: []));

        filtered.ShouldAllBe(h => h.SourceId == second.Id);
        filtered.ShouldNotBeEmpty();
        unfiltered.Select(h => h.SourceId).Distinct().ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Min_score_filters_weak_hits()
    {
        await using var context = new KnowledgeTestContext();
        var relevant = await context.SeedTextAsync("Relevant", "kubernetes cluster autoscaling with node pools");
        var unrelated = await context.SeedTextAsync("Unrelated", "banana bread recipe with walnuts");
        await context.ProcessAsync(relevant.Id, Ct);
        await context.ProcessAsync(unrelated.Id, Ct);

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("kubernetes cluster autoscaling", MinScore: 0.5));

        hits.ShouldHaveSingleItem().SourceId.ShouldBe(relevant.Id);
        hits[0].Score.ShouldBeGreaterThanOrEqualTo(0.5);
    }

    [Fact]
    public async Task Hits_of_deleted_sources_are_dropped()
    {
        await using var context = new KnowledgeTestContext();
        var kept = await context.SeedTextAsync("Kept", "orphan detection keeps this chunk");
        await context.ProcessAsync(kept.Id, Ct);
        await InsertChunkAsync(context, Guid.NewGuid(), 0, "orphan detection drops this chunk", new Dictionary<string, string> { [ChunkMetadataKeys.Title] = "Ghost" });

        var hits = await context.SearchAsync(new KnowledgeSearchRequest("orphan detection chunk"));

        hits.ShouldHaveSingleItem().SourceId.ShouldBe(kept.Id);
    }

    [Fact]
    public async Task Repository_outage_falls_back_to_chunk_metadata()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedTextAsync("Resilient", "search keeps working during a database outage");
        await context.ProcessAsync(source.Id, Ct);
        context.Repository.GetFailure = new InvalidOperationException("database down");

        var hit = (await context.SearchAsync(new KnowledgeSearchRequest("database outage"))).ShouldHaveSingleItem();

        hit.SourceTitle.ShouldBe("Resilient");
    }

    [Fact]
    public async Task Untitled_chunks_without_a_source_title_get_a_placeholder()
    {
        await using var context = new KnowledgeTestContext();
        var source = await context.SeedAsync(new Source { Type = SourceType.Text, Title = "" });
        await InsertChunkAsync(context, source.Id, 0, "nameless content", new Dictionary<string, string>());

        var hit = (await context.SearchAsync(new KnowledgeSearchRequest("nameless content"))).ShouldHaveSingleItem();

        hit.SourceTitle.ShouldBe("Untitled source");
        hit.SourceUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_queries_are_rejected(string query)
    {
        await using var context = new KnowledgeTestContext();

        var ex = await Should.ThrowAsync<ValidationFailedException>(() => context.SearchAsync(new KnowledgeSearchRequest(query)));

        ex.Errors.Keys.ShouldBe(["query"]);
        context.Embeddings.Calls.ShouldBeEmpty();
    }

    private static Task InsertChunkAsync(
        KnowledgeTestContext context,
        Guid sourceId,
        int index,
        string content,
        Dictionary<string, string> metadata,
        string? modelKey = null) =>
        context.VectorStore.InsertAsync(
            [new ChunkRecord(Guid.NewGuid(), sourceId, index, content, modelKey ?? context.Embeddings.ModelKey, FakeEmbeddingGenerator.Embed(content, context.Embeddings.Dimensions), metadata)],
            Ct);
}
