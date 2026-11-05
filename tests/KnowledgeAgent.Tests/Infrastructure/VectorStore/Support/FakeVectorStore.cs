using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;

internal sealed class FakeVectorStore : IVectorStore
{
    private int _ensureSchemaCalls;

    public Func<int, CancellationToken, Task> OnEnsureSchema { get; init; } = (_, _) => Task.CompletedTask;

    public bool PingResult { get; init; } = true;

    public int EnsureSchemaCalls => Volatile.Read(ref _ensureSchemaCalls);

    public string ProviderName => "Fake";

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        OnEnsureSchema(Interlocked.Increment(ref _ensureSchemaCalls), cancellationToken);

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(PingResult);

    public Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
