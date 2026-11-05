using KnowledgeAgent.Infrastructure.VectorStore.InMemory;
using static KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.TestChunks;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore;

public sealed class InMemoryVectorStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoredChunks_AreIsolatedFromLaterChangesToTheCallersObjects()
    {
        var store = new InMemoryVectorStore();
        var vector = new float[] { 1, 0, 0, 0 };
        var metadata = new Dictionary<string, string> { ["title"] = "original" };
        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, vector, metadata: metadata)], Ct);

        vector[0] = -1;
        vector[1] = 5;
        metadata["title"] = "changed";
        metadata["extra"] = "added";

        var hit = (await store.SearchAsync(Query([1, 0, 0, 0]), Ct)).ShouldHaveSingleItem();
        hit.Score.ShouldBe(1d);
        hit.Metadata.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, string>("title", "original"));
    }

    [Fact]
    public async Task ReturnedMetadata_IsReadOnly()
    {
        var store = new InMemoryVectorStore();
        var sourceId = Guid.NewGuid();
        await store.InsertAsync([Chunk(sourceId, 0, [1, 0, 0, 0], metadata: new Dictionary<string, string> { ["title"] = "Guide" })], Ct);

        var hit = (await store.GetChunksAsync(sourceId, cancellationToken: Ct)).ShouldHaveSingleItem();

        Should.Throw<NotSupportedException>(() => ((IDictionary<string, string>)hit.Metadata)["title"] = "changed");
    }

    [Fact]
    public async Task IdenticalVectors_ScoreExactlyOne()
    {
        var store = new InMemoryVectorStore();
        var vector = RandomUnitVector(new Random(3), 1536);
        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, vector, "test:exact:1536")], Ct);

        var hit = (await store.SearchAsync(new(vector, "test:exact:1536", 1), Ct)).ShouldHaveSingleItem();

        hit.Score.ShouldBe(1d);
    }

    [Fact]
    public async Task Operations_HonourCancellation()
    {
        var store = new InMemoryVectorStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;

        await Should.ThrowAsync<OperationCanceledException>(() => store.EnsureSchemaAsync(token));
        await Should.ThrowAsync<OperationCanceledException>(() => store.InsertAsync([Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0])], token));
        await Should.ThrowAsync<OperationCanceledException>(() => store.SearchAsync(Query([1, 0, 0, 0]), token));
        await Should.ThrowAsync<OperationCanceledException>(() => store.GetChunksAsync(Guid.NewGuid(), cancellationToken: token));
        await Should.ThrowAsync<OperationCanceledException>(() => store.CountAsync(cancellationToken: token));
        await Should.ThrowAsync<OperationCanceledException>(() => store.DeleteBySourceAsync(Guid.NewGuid(), token));
        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(0);
    }
}
