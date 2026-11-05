using System.Diagnostics;
using System.Text.RegularExpressions;
using KnowledgeAgent.Infrastructure.VectorStore;
using static KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.TestChunks;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Contract;

public abstract class VectorStoreContractTests : IAsyncDisposable
{
    private const double ScoreTolerance = 1e-5;

    // Targets a few seconds for 1,000 chunks of 384 dimensions, with headroom for a busy CI machine.
    private static readonly TimeSpan PerformanceBudget = TimeSpan.FromSeconds(10);

    private readonly List<IVectorStore> _stores = [];

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract string ExpectedProviderName { get; }

    protected abstract Task<IVectorStore> CreateEmptyStoreAsync(CancellationToken cancellationToken);

    public ValueTask DisposeAsync()
    {
        foreach (var store in _stores)
        {
            (store as IDisposable)?.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ProviderName_IdentifiesTheImplementation()
    {
        var store = await CreateStoreAsync();

        store.ProviderName.ShouldBe(ExpectedProviderName);
    }

    [Fact]
    public async Task Ping_ReturnsTrue()
    {
        var store = await CreateStoreAsync();

        (await store.PingAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureSchema_IsIdempotent_AndKeepsExistingChunks()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        await store.InsertAsync([Chunk(sourceId, 0, [1, 0, 0, 0])], Ct);

        await store.EnsureSchemaAsync(Ct);
        await store.EnsureSchemaAsync(Ct);

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(1);
        await store.InsertAsync([Chunk(sourceId, 1, [0, 1, 0, 0])], Ct);
        (await store.CountAsync(sourceId, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Insert_ThenCount_ReturnsTotalAndPerSourceCounts()
    {
        var store = await CreateStoreAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await store.InsertAsync([.. Enumerable.Range(0, 3).Select(i => Chunk(first, i, [i + 1, 1, 0, 0]))], Ct);
        await store.InsertAsync([.. Enumerable.Range(0, 5).Select(i => Chunk(second, i, [1, i + 1, 0, 0], ModelB))], Ct);

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(8);
        (await store.CountAsync(first, Ct)).ShouldBe(3);
        (await store.CountAsync(second, Ct)).ShouldBe(5);
        (await store.CountAsync(Guid.NewGuid(), Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Insert_EmptyBatch_IsNoOp()
    {
        var store = await CreateStoreAsync();

        await store.InsertAsync([], Ct);

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Insert_InvalidChunk_ThrowsBeforeStoringAnything()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var valid = Chunk(sourceId, 0, [1, 0, 0, 0]);

        await Should.ThrowAsync<ArgumentException>(() => store.InsertAsync([valid, Chunk(sourceId, -1, [1, 0, 0, 0])], Ct));
        await Should.ThrowAsync<ArgumentException>(() => store.InsertAsync([valid, Chunk(sourceId, 1, [])], Ct));
        await Should.ThrowAsync<ArgumentException>(() => store.InsertAsync([valid, Chunk(sourceId, 1, [1, 0, 0, 0], model: " ")], Ct));
        await Should.ThrowAsync<ArgumentException>(() => store.InsertAsync([valid, Chunk(sourceId, 1, new float[70_000])], Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => store.InsertAsync(null!, Ct));

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Search_RanksMostSimilarFirst_AndScoresIdenticalVectorAsOne()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var metadata = new Dictionary<string, string> { [ChunkMetadataKeys.Title] = "Guide" };
        var identical = Chunk(sourceId, 0, [1, 0, 0, 0], content: "identical", metadata: metadata);
        var close = Chunk(sourceId, 1, [0.9f, 0.1f, 0, 0]);
        var orthogonal = Chunk(sourceId, 2, [0, 1, 0, 0]);
        var opposite = Chunk(sourceId, 3, [-1, 0, 0, 0]);
        await store.InsertAsync([orthogonal, opposite, close, identical], Ct);

        var hits = await store.SearchAsync(Query([1, 0, 0, 0]), Ct);

        hits.Select(hit => hit.ChunkId).ShouldBe([identical.ChunkId, close.ChunkId, orthogonal.ChunkId, opposite.ChunkId]);
        hits[0].Score.ShouldBe(1d, ScoreTolerance);
        hits[1].Score.ShouldBe(Cosine([1, 0, 0, 0], [0.9f, 0.1f, 0, 0]), ScoreTolerance);
        hits[2].Score.ShouldBe(0d, ScoreTolerance);
        hits[3].Score.ShouldBe(-1d, ScoreTolerance);

        var top = hits[0];
        top.SourceId.ShouldBe(sourceId);
        top.ChunkIndex.ShouldBe(0);
        top.Content.ShouldBe("identical");
        top.Metadata.ShouldBe(metadata);
    }

    [Fact]
    public async Task Search_ScoreIsCosineSimilarity_IndependentOfVectorLength()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var scaled = Chunk(sourceId, 0, [5, 0, 0, 0]);
        var angled = Chunk(sourceId, 1, [3, 4, 0, 0]);
        await store.InsertAsync([scaled, angled], Ct);

        var hits = await store.SearchAsync(Query([0.5f, 0, 0, 0]), Ct);

        hits.Select(hit => hit.ChunkId).ShouldBe([scaled.ChunkId, angled.ChunkId]);
        hits[0].Score.ShouldBe(1d, ScoreTolerance);
        hits[1].Score.ShouldBe(0.6, ScoreTolerance);
    }

    [Fact]
    public async Task Search_ZeroVectors_ScoreZeroInsteadOfNaN()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var unit = Chunk(sourceId, 0, [1, 0, 0, 0]);
        var zero = Chunk(sourceId, 1, [0, 0, 0, 0]);
        await store.InsertAsync([unit, zero], Ct);

        var zeroQueryHits = await store.SearchAsync(Query([0, 0, 0, 0]), Ct);
        var unitQueryHits = await store.SearchAsync(Query([1, 0, 0, 0]), Ct);

        zeroQueryHits.Count.ShouldBe(2);
        zeroQueryHits.ShouldAllBe(hit => hit.Score == 0d);
        unitQueryHits.Select(hit => hit.ChunkId).ShouldBe([unit.ChunkId, zero.ChunkId]);
        unitQueryHits[1].Score.ShouldBe(0d);
    }

    [Fact]
    public async Task Search_NeverReturnsChunksOfAnotherEmbeddingModel()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var modelA = Chunk(sourceId, 0, [1, 0, 0, 0], ModelA);
        var modelB = Chunk(sourceId, 1, [1, 0, 0, 0], ModelB);
        await store.InsertAsync([modelA, modelB], Ct);

        (await store.SearchAsync(Query([1, 0, 0, 0], model: ModelA), Ct)).ShouldHaveSingleItem().ChunkId.ShouldBe(modelA.ChunkId);
        (await store.SearchAsync(Query([1, 0, 0, 0], model: ModelB), Ct)).ShouldHaveSingleItem().ChunkId.ShouldBe(modelB.ChunkId);
        (await store.SearchAsync(Query([1, 0, 0, 0], model: "test:other-model:4"), Ct)).ShouldBeEmpty();
        (await store.SearchAsync(Query([1, 0, 0, 0], model: ModelA.ToUpperInvariant()), Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_OnlyComparesVectorsOfTheQueryDimensionality()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        const string sharedModel = "test:shared-key";
        var fourDimensions = Chunk(sourceId, 0, [1, 0, 0, 0], sharedModel);
        var eightDimensions = Chunk(sourceId, 1, [1, 0, 0, 0, 0, 0, 0, 0], sharedModel);
        await store.InsertAsync([fourDimensions, eightDimensions], Ct);

        (await store.SearchAsync(Query([1, 0, 0, 0], model: sharedModel), Ct))
            .ShouldHaveSingleItem().ChunkId.ShouldBe(fourDimensions.ChunkId);
        (await store.SearchAsync(Query([1, 0, 0, 0, 0, 0, 0, 0], model: sharedModel, minScore: 0.5), Ct))
            .ShouldHaveSingleItem().ChunkId.ShouldBe(eightDimensions.ChunkId);
        (await store.SearchAsync(Query([1, 0], model: sharedModel), Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_FiltersBySourceIds()
    {
        var store = await CreateStoreAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await store.InsertAsync(
        [
            Chunk(first, 0, [1, 0.1f, 0, 0]),
            Chunk(first, 1, [1, 0.2f, 0, 0]),
            Chunk(second, 0, [1, 0.3f, 0, 0]),
            Chunk(third, 0, [1, 0.4f, 0, 0]),
        ], Ct);

        var onlyFirst = await store.SearchAsync(Query([1, 0, 0, 0], sourceIds: [first]), Ct);
        var firstAndThird = await store.SearchAsync(Query([1, 0, 0, 0], sourceIds: [third, first, first]), Ct);
        var unknown = await store.SearchAsync(Query([1, 0, 0, 0], sourceIds: [Guid.NewGuid()]), Ct);

        onlyFirst.Select(hit => (hit.SourceId, hit.ChunkIndex)).ShouldBe([(first, 0), (first, 1)]);
        firstAndThird.Select(hit => (hit.SourceId, hit.ChunkIndex)).ShouldBe([(first, 0), (first, 1), (third, 0)]);
        unknown.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_EmptySourceFilter_SearchesAllSources()
    {
        var store = await CreateStoreAsync();
        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0]), Chunk(Guid.NewGuid(), 0, [0, 1, 0, 0])], Ct);

        var hits = await store.SearchAsync(Query([1, 0, 0, 0], sourceIds: []), Ct);

        hits.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_WithThousandsOfSourceIds_ReturnsTopKAcrossAllOfThem()
    {
        var store = await CreateStoreAsync();
        var sourceIds = Enumerable.Range(0, 2500).Select(_ => Guid.NewGuid()).ToArray();
        var best = Chunk(sourceIds[2400], 0, [1, 0.1f, 0, 0]);
        var second = Chunk(sourceIds[1200], 0, [1, 0.2f, 0, 0]);
        var third = Chunk(sourceIds[0], 0, [1, 0.3f, 0, 0]);
        var unlisted = Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0]);
        await store.InsertAsync([third, second, best, unlisted], Ct);

        var hits = await store.SearchAsync(Query([1, 0, 0, 0], topK: 2, sourceIds: sourceIds), Ct);

        hits.Select(hit => hit.ChunkId).ShouldBe([best.ChunkId, second.ChunkId]);
    }

    [Fact]
    public async Task Search_AppliesMinScore()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var identical = Chunk(sourceId, 0, [1, 0, 0, 0]);
        var close = Chunk(sourceId, 1, [0.9f, 0.1f, 0, 0]);
        var orthogonal = Chunk(sourceId, 2, [0, 1, 0, 0]);
        var opposite = Chunk(sourceId, 3, [-1, 0, 0, 0]);
        await store.InsertAsync([identical, close, orthogonal, opposite], Ct);

        async Task<Guid[]> SearchIds(double minScore) =>
            [.. (await store.SearchAsync(Query([1, 0, 0, 0], minScore: minScore), Ct)).Select(hit => hit.ChunkId)];

        (await SearchIds(0.5)).ShouldBe([identical.ChunkId, close.ChunkId]);
        (await SearchIds(-0.5)).ShouldBe([identical.ChunkId, close.ChunkId, orthogonal.ChunkId]);
        (await SearchIds(0.99999)).ShouldBe([identical.ChunkId]);
        (await SearchIds(-2)).Length.ShouldBe(4);
        (await SearchIds(1.5)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_ReturnsTopK_ClampedToOneThroughHundred()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        // Similarity decreases with the chunk index.
        await store.InsertAsync([.. Enumerable.Range(0, 120).Select(i => Chunk(sourceId, i, [1, i / 100f, 0, 0]))], Ct);

        async Task<int[]> SearchIndexes(int topK) =>
            [.. (await store.SearchAsync(Query([1, 0, 0, 0], topK: topK), Ct)).Select(hit => hit.ChunkIndex)];

        (await SearchIndexes(5)).ShouldBe([0, 1, 2, 3, 4]);
        (await SearchIndexes(0)).ShouldBe([0]);
        (await SearchIndexes(-3)).ShouldBe([0]);
        (await SearchIndexes(1000)).ShouldBe(Enumerable.Range(0, 100));
    }

    [Fact]
    public async Task Search_InvalidQuery_Throws()
    {
        var store = await CreateStoreAsync();

        await Should.ThrowAsync<ArgumentException>(() => store.SearchAsync(Query([]), Ct));
        await Should.ThrowAsync<ArgumentException>(() => store.SearchAsync(Query([1, 0, 0, 0], model: ""), Ct));
        await Should.ThrowAsync<ArgumentException>(() => store.SearchAsync(Query([1, 0, 0, 0], minScore: double.NaN), Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => store.SearchAsync(null!, Ct));
    }

    [Fact]
    public async Task DeleteBySource_RemovesChunksImmediately_AndReturnsTheRemovedCount()
    {
        var store = await CreateStoreAsync();
        var deleted = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await store.InsertAsync([.. Enumerable.Range(0, 5).Select(i => Chunk(deleted, i, [1, i / 10f, 0, 0]))], Ct);
        await store.InsertAsync([.. Enumerable.Range(0, 3).Select(i => Chunk(kept, i, [i / 10f, 1, 0, 0]))], Ct);

        var removed = await store.DeleteBySourceAsync(deleted, Ct);

        removed.ShouldBe(5);
        (await store.CountAsync(deleted, Ct)).ShouldBe(0);
        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(3);
        (await store.GetChunksAsync(deleted, cancellationToken: Ct)).ShouldBeEmpty();
        (await store.SearchAsync(Query([1, 0, 0, 0], sourceIds: [deleted]), Ct)).ShouldBeEmpty();
        var remaining = await store.SearchAsync(Query([1, 0, 0, 0]), Ct);
        remaining.Count.ShouldBe(3);
        remaining.ShouldAllBe(hit => hit.SourceId == kept);

        (await store.DeleteBySourceAsync(deleted, Ct)).ShouldBe(0);
        (await store.DeleteBySourceAsync(Guid.NewGuid(), Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteBySource_ThenReinsert_MakesNewChunksVisible()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        await store.InsertAsync([Chunk(sourceId, 0, [1, 0, 0, 0], content: "old")], Ct);

        await store.DeleteBySourceAsync(sourceId, Ct);
        await store.InsertAsync([Chunk(sourceId, 0, [1, 0, 0, 0], content: "new"), Chunk(sourceId, 1, [0, 1, 0, 0], content: "new")], Ct);

        (await store.CountAsync(sourceId, Ct)).ShouldBe(2);
        (await store.SearchAsync(Query([1, 0, 0, 0]), Ct)).ShouldAllBe(hit => hit.Content == "new");
    }

    [Fact]
    public async Task GetChunks_OrdersByChunkIndex_AndPages()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var other = Guid.NewGuid();
        int[] shuffled = [7, 2, 9, 0, 5, 1, 8, 3, 6, 4];
        await store.InsertAsync([.. shuffled.Take(5).Select(i => Chunk(sourceId, i, [1, i, 0, 0]))], Ct);
        await store.InsertAsync([.. shuffled.Skip(5).Select(i => Chunk(sourceId, i, [1, i, 0, 0]))], Ct);
        await store.InsertAsync([.. Enumerable.Range(0, 3).Select(i => Chunk(other, i, [0, 1, i, 0]))], Ct);

        async Task<int[]> Page(int skip, int take) =>
            [.. (await store.GetChunksAsync(sourceId, skip, take, Ct)).Select(hit => hit.ChunkIndex)];

        var all = await store.GetChunksAsync(sourceId, cancellationToken: Ct);
        all.Select(hit => hit.ChunkIndex).ShouldBe(Enumerable.Range(0, 10));
        all.ShouldAllBe(hit => hit.SourceId == sourceId && hit.Score == 0d);
        all[3].Content.ShouldBe($"Chunk 3 of source {sourceId:N}");
        (await Page(3, 4)).ShouldBe([3, 4, 5, 6]);
        (await Page(8, 10)).ShouldBe([8, 9]);
        (await Page(10, 5)).ShouldBeEmpty();
        (await Page(0, 0)).ShouldBeEmpty();
        (await Page(-5, 2)).ShouldBe([0, 1]);
        (await store.GetChunksAsync(Guid.NewGuid(), cancellationToken: Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task MetadataAndUnicodeContent_RoundTrip()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        const string content =
            "Příliš žluťoučký kůň úpěl ďábelské ódy — 你好，世界 — Привет 🚀👩‍💻\n" +
            "\"double\" 'single' `back` \\ back\\slash\t{\"json\": [1, 2]} <b>&amp;</b> ' OR 1=1 --";
        var metadata = new Dictionary<string, string>
        {
            [ChunkMetadataKeys.Title] = "Ünïcødé “title” 🚀",
            [ChunkMetadataKeys.Url] = "https://example.com/a?b=1&c=ž#fragment",
            [ChunkMetadataKeys.Page] = "12",
            [ChunkMetadataKeys.SourceType] = "Pdf",
            ["key with spaces"] = "value \"with\" quotes\nand a newline",
            ["empty"] = string.Empty,
        };
        var rich = Chunk(sourceId, 0, [1, 0, 0, 0], content: content, metadata: metadata);
        var plain = Chunk(sourceId, 1, [0, 1, 0, 0], content: string.Empty);
        await store.InsertAsync([rich, plain], Ct);

        var searched = await store.SearchAsync(Query([1, 0, 0, 0]), Ct);
        var listed = await store.GetChunksAsync(sourceId, cancellationToken: Ct);

        foreach (var hit in new[] { searched[0], listed[0] })
        {
            hit.ChunkId.ShouldBe(rich.ChunkId);
            hit.Content.ShouldBe(content);
            hit.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ShouldBe(metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        listed[1].Content.ShouldBe(string.Empty);
        listed[1].Metadata.ShouldNotBeNull();
        listed[1].Metadata.ShouldBeEmpty();
    }

    [Fact]
    public async Task InsertAndSearch_ThousandChunksOf384Dimensions_FinishWithinBudget()
    {
        var store = await CreateStoreAsync();
        const string model = "test:perf:384";
        var random = new Random(42);
        var sourceIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var chunks = Enumerable.Range(0, 1000)
            .Select(i => Chunk(sourceIds[i % 10], i / 10, RandomUnitVector(random, 384), model))
            .ToArray();
        var target = chunks[637];

        var stopwatch = Stopwatch.StartNew();
        await store.InsertAsync(chunks, Ct);
        var inserted = stopwatch.Elapsed;
        var hits = await store.SearchAsync(new VectorSearchQuery(target.Embedding, model, 10), Ct);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{ExpectedProviderName}: insert {inserted.TotalMilliseconds:0} ms, insert + search {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        hits.Count.ShouldBe(10);
        hits[0].ChunkId.ShouldBe(target.ChunkId);
        hits[0].Score.ShouldBe(1d, ScoreTolerance);
        hits.Select(hit => hit.Score).ShouldBeInOrder(SortDirection.Descending);
        hits[1].Score.ShouldBe(chunks.Where(c => c != target).Max(c => Cosine(target.Embedding.ToArray(), c.Embedding.ToArray())), ScoreTolerance);
        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(1000);
        stopwatch.Elapsed.ShouldBeLessThan(PerformanceBudget);
    }

    [Fact]
    public async Task ConcurrentInsertsAndSearches_AreSafe()
    {
        var store = await CreateStoreAsync();
        const string model = "test:concurrent:16";
        var sourceIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        await Parallel.ForEachAsync(sourceIds, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = Ct }, async (sourceId, ct) =>
        {
            var random = new Random(sourceId.GetHashCode());
            await store.InsertAsync([.. Enumerable.Range(0, 25).Select(i => Chunk(sourceId, i, RandomUnitVector(random, 16), model))], ct);

            var hits = await store.SearchAsync(new VectorSearchQuery(RandomUnitVector(random, 16), model, 5, [sourceId]), ct);
            hits.Count.ShouldBe(5);
            hits.ShouldAllBe(hit => hit.SourceId == sourceId);
            (await store.CountAsync(sourceId, ct)).ShouldBe(25);
        });

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(200);
    }

    [Fact]
    public async Task KeywordSearch_RanksByBm25OverWordStartMatches()
    {
        var store = await CreateStoreAsync();
        var handbook = Guid.NewGuid();
        var other = Guid.NewGuid();
        string[] contents =
        [
            "The vacation policy grants every employee 27 vacation days per year.",
            "Vacation requests need approval from your manager.",
            "A staycation is not counted as leave.",
            "Disapproval of a request must be explained in writing.",
            "Parking spaces are assigned by the facility team.",
        ];
        var chunks = contents.Select((content, index) => Chunk(index < 4 ? handbook : other, index, [1, 0, 0, 0], content: content)).ToArray();
        await store.InsertAsync(chunks, Ct);
        await store.InsertAsync([Chunk(handbook, 9, [1, 0, 0, 0], ModelB, content: "vacation vacation vacation approval")], Ct);

        var hits = await store.KeywordSearchAsync(new KeywordSearchQuery(["vacation", "approv"], ModelA, 10), Ct);

        // "staycation" and "Disapproval" match only mid-word, and the ModelB chunk belongs to another model.
        hits.Select(hit => hit.ChunkId).ShouldBe([chunks[1].ChunkId, chunks[0].ChunkId]);
        var expected = Bm25(contents, ["vacation", "approv"]);
        hits[0].Score.ShouldBe(expected[1], 1e-6);
        hits[1].Score.ShouldBe(expected[0], 1e-6);
        hits[0].Content.ShouldBe(contents[1]);
        hits[0].SourceId.ShouldBe(handbook);
    }

    [Fact]
    public async Task KeywordSearch_MatchesIdentifiersWithPunctuationAndNonAsciiLetters()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var chunks = new[]
        {
            Chunk(sourceId, 0, [1, 0, 0, 0], content: "Error E-214 means an encoder mismatch on joint 2."),
            Chunk(sourceId, 1, [1, 0, 0, 0], content: "Code XE-214 belongs to another product."),
            Chunk(sourceId, 2, [1, 0, 0, 0], content: "Pick up the laptop in room B2.17 on the second floor."),
            Chunk(sourceId, 3, [1, 0, 0, 0], content: "Room B2X17 is a storage room."),
            Chunk(sourceId, 4, [1, 0, 0, 0], content: "PRÍRUČKA pre zamestnancov: Čistenie robota raz týždenne."),
            Chunk(sourceId, 5, [1, 0, 0, 0], content: "Predčistenie nie je čistenie? Áno."),
        };
        await store.InsertAsync(chunks, Ct);

        async Task<Guid[]> FindAsync(params string[] terms) =>
            [.. (await store.KeywordSearchAsync(new KeywordSearchQuery(terms, ModelA, 10), Ct)).Select(hit => hit.ChunkId)];

        (await FindAsync("e-214")).ShouldBe([chunks[0].ChunkId]);
        (await FindAsync("b2.17")).ShouldBe([chunks[2].ChunkId]);
        (await FindAsync("príručka")).ShouldBe([chunks[4].ChunkId]);
        (await FindAsync("čistenie")).ShouldBe([chunks[4].ChunkId, chunks[5].ChunkId], ignoreOrder: true);
        (await FindAsync("E-214 ")).ShouldBe([chunks[0].ChunkId], "terms are trimmed and lower-cased");
    }

    [Fact]
    public async Task KeywordSearch_SourceFilterKeepsGlobalStatistics()
    {
        var store = await CreateStoreAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await store.InsertAsync(
        [
            Chunk(first, 0, [1, 0, 0, 0], content: "Robots need maintenance every 2,000 operating hours."),
            Chunk(first, 1, [1, 0, 0, 0], content: "Maintenance logs are kept for five years."),
            Chunk(second, 0, [1, 0, 0, 0], content: "Maintenance windows are announced a week ahead."),
        ], Ct);

        var all = await store.KeywordSearchAsync(new KeywordSearchQuery(["maintenance"], ModelA, 10), Ct);
        var filtered = await store.KeywordSearchAsync(new KeywordSearchQuery(["maintenance"], ModelA, 10, [second]), Ct);

        all.Count.ShouldBe(3);
        filtered.ShouldHaveSingleItem().SourceId.ShouldBe(second);
        filtered[0].Score.ShouldBe(all.Single(hit => hit.SourceId == second).Score, 1e-9);
    }

    [Fact]
    public async Task KeywordSearch_WithoutTermsOrMatches_ReturnsNothing()
    {
        var store = await CreateStoreAsync();
        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0], content: "Some content.")], Ct);

        (await store.KeywordSearchAsync(new KeywordSearchQuery([], ModelA, 5), Ct)).ShouldBeEmpty();
        (await store.KeywordSearchAsync(new KeywordSearchQuery(["  "], ModelA, 5), Ct)).ShouldBeEmpty();
        (await store.KeywordSearchAsync(new KeywordSearchQuery(["missing"], ModelA, 5), Ct)).ShouldBeEmpty();
        (await store.KeywordSearchAsync(new KeywordSearchQuery(["content"], "unknown:model:4", 5), Ct)).ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => store.KeywordSearchAsync(new KeywordSearchQuery(["content"], " ", 5), Ct));
    }

    [Fact]
    public async Task KeywordSearch_ReturnsTheTopK()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 12)
            .Select(i => Chunk(sourceId, i, [1, 0, 0, 0], content: string.Join(' ', Enumerable.Repeat("torque", i + 1)) + " sensor calibration notes"))
            .ToArray();
        await store.InsertAsync(chunks, Ct);

        var hits = await store.KeywordSearchAsync(new KeywordSearchQuery(["torque"], ModelA, 3), Ct);

        hits.Count.ShouldBe(3);
        hits.Select(hit => hit.Score).ShouldBeInOrder(SortDirection.Descending);
        hits[0].ChunkId.ShouldBe(chunks[11].ChunkId);
    }

    [Fact]
    public async Task TextSearch_FindsEveryChunkWithTheText_OncePerChunk_GroupedBySourceInChunkOrder()
    {
        var store = await CreateStoreAsync();
        var handbook = Guid.NewGuid();
        var other = Guid.NewGuid();
        var chunks = new[]
        {
            Chunk(handbook, 0, [1, 0, 0, 0], content: "The vacation policy grants 27 vacation days."),
            Chunk(handbook, 1, [1, 0, 0, 0], content: "A staycation is not counted as leave."),
            Chunk(handbook, 2, [1, 0, 0, 0], content: "Plan your Vacation early."),
            Chunk(other, 0, [1, 0, 0, 0], content: "Vacation requests need approval."),
            Chunk(other, 1, [1, 0, 0, 0], content: "Parking is free."),
        };
        await store.InsertAsync(chunks, Ct);
        await store.InsertAsync([Chunk(handbook, 2, [0, 1, 0, 0], ModelB, content: "Plan your Vacation early.")], Ct);

        var page = await store.FindTextAsync(new TextSearchQuery("vacation"), Ct);

        // "staycation" matches only inside a word, and the ModelB copy of chunk 2 counts once.
        (page.TotalChunks, page.TotalSources).ShouldBe((3L, 2L));
        page.Items.Select(hit => (hit.SourceId, hit.ChunkIndex)).ShouldBe([(handbook, 0), (handbook, 2), (other, 0)], ignoreOrder: true);
        page.Items.Zip(page.Items.Skip(1)).Count(pair => pair.First.SourceId != pair.Second.SourceId).ShouldBe(1);
        page.Items.Where(hit => hit.SourceId == handbook).Select(hit => hit.ChunkId).ShouldBe([chunks[0].ChunkId, chunks[2].ChunkId]);
        page.Items.Single(hit => hit.SourceId == other).Content.ShouldBe("Vacation requests need approval.");
    }

    [Fact]
    public async Task TextSearch_MatchesLiterally_WithOptionalCaseAndWholeWords()
    {
        var store = await CreateStoreAsync();
        var sourceId = Guid.NewGuid();
        string[] contents =
        [
            "The art of design: Art Deco.",
            "An article about parts.",
            "Report by Jan\nNovák, mayor.",
            "Error E-214 means an encoder mismatch.",
            "Code XE-214 belongs to another product.",
            "PRÍRUČKA: Čistenie robota.",
            "Predčistenie nie je povinné.",
            "C++ (beta) costs $5.00 [draft] {x} a|b.",
            "„Quoted“ text – with dashes.",
            "The US office.",
            "Call us today.",
        ];
        await store.InsertAsync(contents.Select((content, index) => Chunk(sourceId, index, [1, 0, 0, 0], content: content)).ToArray(), Ct);

        async Task<int[]> FindAsync(string text, bool matchCase = false, bool wholeWords = true) =>
            [.. (await store.FindTextAsync(new TextSearchQuery(text, matchCase, wholeWords), Ct)).Items.Select(hit => hit.ChunkIndex)];

        (await FindAsync("art")).ShouldBe([0]);
        (await FindAsync("art", wholeWords: false)).ShouldBe([0, 1]);
        (await FindAsync(" jan  novák ")).ShouldBe([2], "whitespace matches any whitespace, including a line break");
        (await FindAsync("e-214")).ShouldBe([3]);
        (await FindAsync("e-214", wholeWords: false)).ShouldBe([3, 4]);
        (await FindAsync("čistenie")).ShouldBe([5]);
        (await FindAsync("čistenie", wholeWords: false)).ShouldBe([5, 6]);
        (await FindAsync("príručka", matchCase: true)).ShouldBeEmpty();
        (await FindAsync("PRÍRUČKA", matchCase: true)).ShouldBe([5]);
        (await FindAsync("C++ (beta) costs $5.00 [draft] {x} a|b.")).ShouldBe([7], "regular expression syntax is literal text");
        (await FindAsync("$5.00")).ShouldBe([7]);
        (await FindAsync("„Quoted“ text – with")).ShouldBe([8], "non-ASCII punctuation is literal text");
        (await FindAsync("US", matchCase: true)).ShouldBe([9]);
        (await FindAsync("us")).ShouldBe([9, 10]);
        (await FindAsync(" \t ")).ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => store.FindTextAsync(new TextSearchQuery(new string('a', 201)), Ct));
    }

    [Fact]
    public async Task TextSearch_CountsBySource_MostMatchingChunksFirst_AndPagesBothViews()
    {
        var store = await CreateStoreAsync();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await store.InsertAsync(
        [
            Chunk(a, 0, [1, 0, 0, 0], content: "Torque limits for joint 1."),
            Chunk(a, 1, [1, 0, 0, 0], content: "Torque limits for joint 2."),
            Chunk(a, 2, [1, 0, 0, 0], content: "Torque limits for joint 3."),
            Chunk(b, 0, [1, 0, 0, 0], content: "Torque is measured in Nm."),
            Chunk(c, 0, [1, 0, 0, 0], content: "Torque sensors drift."),
            Chunk(c, 1, [1, 0, 0, 0], content: "Calibrate the torque sensor yearly."),
            Chunk(c, 2, [1, 0, 0, 0], content: "No match here."),
        ], Ct);

        var sources = await store.CountTextBySourceAsync(new TextSearchQuery("torque"), Ct);
        var secondSource = await store.CountTextBySourceAsync(new TextSearchQuery("torque", Skip: 1, Take: 1), Ct);

        sources.Items.ShouldBe([new SourceTextMatches(a, 3), new SourceTextMatches(c, 2), new SourceTextMatches(b, 1)]);
        (sources.TotalChunks, sources.TotalSources).ShouldBe((6L, 3L));
        secondSource.Items.ShouldBe([new SourceTextMatches(c, 2)]);
        (secondSource.TotalChunks, secondSource.TotalSources).ShouldBe((6L, 3L));

        var all = (await store.FindTextAsync(new TextSearchQuery("torque", Take: 100), Ct)).Items;
        var paged = new List<VectorSearchHit>();
        for (var skip = 0; skip < 6; skip += 4)
        {
            var page = await store.FindTextAsync(new TextSearchQuery("torque", Skip: skip, Take: 4), Ct);
            page.TotalChunks.ShouldBe(6);
            paged.AddRange(page.Items);
        }

        all.Count.ShouldBe(6);
        paged.Select(hit => hit.ChunkId).ShouldBe(all.Select(hit => hit.ChunkId));
        (await store.FindTextAsync(new TextSearchQuery("torque", Skip: 6), Ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task TextSearch_FiltersBySourceIds_AlsoWithThousandsOfThem()
    {
        var store = await CreateStoreAsync();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await store.InsertAsync(
        [
            Chunk(a, 0, [1, 0, 0, 0], content: "Invoice 2026-114 was paid."),
            Chunk(a, 1, [1, 0, 0, 0], content: "See invoice 2026-114 again."),
            Chunk(b, 0, [1, 0, 0, 0], content: "Invoice 2026-114 is disputed."),
            Chunk(c, 0, [1, 0, 0, 0], content: "Invoice 2026-114 was cancelled."),
        ], Ct);
        var manyIds = Enumerable.Range(0, 2500).Select(_ => Guid.NewGuid()).Append(a).Append(c).ToArray();

        var filtered = await store.FindTextAsync(new TextSearchQuery("2026-114", SourceIds: [a, c]), Ct);
        var walked = new List<VectorSearchHit>();
        for (var skip = 0; skip < 4; skip++)
        {
            var page = await store.FindTextAsync(new TextSearchQuery("2026-114", SourceIds: manyIds, Skip: skip, Take: 1), Ct);
            (page.TotalChunks, page.TotalSources).ShouldBe((3L, 2L));
            walked.AddRange(page.Items);
        }

        var counts = await store.CountTextBySourceAsync(new TextSearchQuery("2026-114", SourceIds: manyIds), Ct);

        filtered.Items.Select(hit => hit.SourceId).ShouldBe([a, a, c], ignoreOrder: true);
        (filtered.TotalChunks, filtered.TotalSources).ShouldBe((3L, 2L));
        walked.Select(hit => hit.ChunkId).ShouldBe(filtered.Items.Select(hit => hit.ChunkId), ignoreOrder: true);
        counts.Items.ShouldBe([new SourceTextMatches(a, 2), new SourceTextMatches(c, 1)]);
        (counts.TotalChunks, counts.TotalSources).ShouldBe((3L, 2L));
    }

    // Reference BM25 (k1 = 1.2, b = 0.75) over word-start matches, independent of the store implementations.
    private static double[] Bm25(IReadOnlyList<string> contents, IReadOnlyList<string> terms)
    {
        var lengths = contents.Select(content => content.EnumerateRunes().Count()).ToArray();
        var averageLength = lengths.Average();
        var frequencies = terms
            .Select(term => contents.Select(content => Regex.Count(
                content,
                @"(?:^|[^\p{L}\p{N}])" + Regex.Escape(term),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray())
            .ToArray();
        return contents.Select((_, document) => frequencies.Sum(termFrequencies =>
        {
            var tf = termFrequencies[document];
            var df = termFrequencies.Count(count => count > 0);
            var idf = Math.Log(1 + (contents.Count - df + 0.5) / (df + 0.5));
            return tf == 0 ? 0 : idf * tf * 2.2 / (tf + 1.2 * (0.25 + 0.75 * lengths[document] / averageLength));
        })).ToArray();
    }

    private async Task<IVectorStore> CreateStoreAsync()
    {
        var store = await CreateEmptyStoreAsync(Ct);
        _stores.Add(store);
        return store;
    }
}
