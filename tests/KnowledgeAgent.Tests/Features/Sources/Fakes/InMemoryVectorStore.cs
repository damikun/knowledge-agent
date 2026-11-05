using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Tests.Features.Sources.Fakes;

internal sealed class InMemoryVectorStore : IVectorStore
{
    private readonly Lock _gate = new();
    private readonly List<ChunkRecord> _chunks = [];

    public string ProviderName => "InMemoryTest";

    public int InsertCalls { get; private set; }

    public ConcurrentQueue<KeywordSearchQuery> KeywordCalls { get; } = new();

    public Exception? KeywordFailure { get; set; }

    public Func<IReadOnlyList<ChunkRecord>, Task>? AfterInsert { get; set; }

    public IReadOnlyList<ChunkRecord> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _chunks];
            }
        }
    }

    public IReadOnlyList<ChunkRecord> ForSource(Guid sourceId) =>
        [.. All.Where(c => c.SourceId == sourceId).OrderBy(c => c.ChunkIndex)];

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _chunks.AddRange(chunks);
            InsertCalls++;
        }

        if (AfterInsert is { } afterInsert)
        {
            await afterInsert(chunks);
        }
    }

    public Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult((long)_chunks.RemoveAll(c => c.SourceId == sourceId));
        }
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<VectorSearchHit> hits = [.. _chunks
                .Where(c => c.EmbeddingModel == query.EmbeddingModel)
                .Where(c => query.SourceIds is null || query.SourceIds.Contains(c.SourceId))
                .Select(c => (Chunk: c, Score: Cosine(c.Embedding.Span, query.Vector.Span)))
                .Where(x => query.MinScore is null || x.Score >= query.MinScore)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Chunk.ChunkIndex)
                .Take(query.TopK)
                .Select(x => new VectorSearchHit(x.Chunk.ChunkId, x.Chunk.SourceId, x.Chunk.ChunkIndex, x.Chunk.Content, x.Score, x.Chunk.Metadata))];
            return Task.FromResult(hits);
        }
    }

    public Task<IReadOnlyList<VectorSearchHit>> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken = default)
    {
        KeywordCalls.Enqueue(query);
        if (KeywordFailure is { } failure)
        {
            throw failure;
        }

        var patterns = query.Terms
            .Select(term => new Regex(@"(?:^|[^\p{L}\p{N}])" + Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        lock (_gate)
        {
            IReadOnlyList<VectorSearchHit> hits = [.. _chunks
                .Where(c => c.EmbeddingModel == query.EmbeddingModel)
                .Where(c => query.SourceIds is not { Count: > 0 } || query.SourceIds.Contains(c.SourceId))
                .Select(c => (Chunk: c, Score: (double)patterns.Sum(pattern => pattern.Count(c.Content))))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Chunk.ChunkIndex)
                .Take(query.TopK)
                .Select(x => new VectorSearchHit(x.Chunk.ChunkId, x.Chunk.SourceId, x.Chunk.ChunkIndex, x.Chunk.Content, x.Score, x.Chunk.Metadata))];
            return Task.FromResult(hits);
        }
    }

    public Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<VectorSearchHit> hits = [.. _chunks
                .Where(c => c.SourceId == sourceId)
                .OrderBy(c => c.ChunkIndex)
                .Skip(skip)
                .Take(take)
                .Select(c => new VectorSearchHit(c.ChunkId, c.SourceId, c.ChunkIndex, c.Content, 0, c.Metadata))];
            return Task.FromResult(hits);
        }
    }

    public Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult((long)_chunks.Count(c => sourceId is null || c.SourceId == sourceId));
        }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
