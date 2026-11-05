using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Infrastructure.VectorStore.InMemory;

// Readers use an immutable snapshot without locks; writers are serialized and swap in a new one.
// Append-only like the ClickHouse table.
internal sealed class InMemoryVectorStore : IVectorStore
{
    private static readonly IReadOnlyList<VectorSearchHit> NoHits = [];

    private readonly Lock _writeLock = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public string ProviderName => VectorStoreProviders.InMemory;

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task InsertAsync(IReadOnlyList<ChunkRecord> chunks, CancellationToken cancellationToken = default)
    {
        VectorStoreGuard.ValidateChunks(chunks);
        cancellationToken.ThrowIfCancellationRequested();
        if (chunks.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Copy outside the lock: callers may reuse their vectors and dictionaries after the call returns.
        var stored = new StoredChunk[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            stored[i] = StoredChunk.From(chunks[i]);
        }

        lock (_writeLock)
        {
            var current = _snapshot;
            var bySource = current.BySource.ToBuilder();
            foreach (var group in stored.GroupBy(chunk => chunk.SourceId))
            {
                bySource[group.Key] = bySource.TryGetValue(group.Key, out var existing)
                    ? existing.AddRange(group)
                    : [.. group];
            }

            _snapshot = new Snapshot(bySource.ToImmutable(), current.Count + stored.Length);
        }

        return Task.CompletedTask;
    }

    public Task<long> DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_writeLock)
        {
            var current = _snapshot;
            if (!current.BySource.TryGetValue(sourceId, out var removed))
            {
                return Task.FromResult(0L);
            }

            _snapshot = new Snapshot(current.BySource.Remove(sourceId), current.Count - removed.Length);
            return Task.FromResult((long)removed.Length);
        }
    }

    public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeSearch(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (search.MatchesNothing)
        {
            return Task.FromResult(NoHits);
        }

        var snapshot = _snapshot;
        var vector = search.Vector.Span;
        var vectorSumOfSquares = VectorMath.SumOfSquares(vector);

        // Bounded heap that always evicts the worst candidate, so memory stays O(TopK).
        var best = new PriorityQueue<Candidate, Candidate>(search.TopK + 1, WorstFirstComparer.Instance);
        foreach (var bucket in SelectBuckets(snapshot, search.SourceIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var chunk in bucket)
            {
                if (chunk.Embedding.Length != vector.Length
                    || !string.Equals(chunk.EmbeddingModel, search.EmbeddingModel, StringComparison.Ordinal))
                {
                    continue;
                }

                var score = VectorMath.CosineSimilarity(vector, vectorSumOfSquares, chunk.Embedding, chunk.SumOfSquares);
                if (score < search.MinScore)
                {
                    continue;
                }

                var candidate = new Candidate(chunk, score);
                best.Enqueue(candidate, candidate);
                if (best.Count > search.TopK)
                {
                    best.Dequeue();
                }
            }
        }

        // The heap yields the worst candidate first, so the result is filled from the end.
        var hits = new VectorSearchHit[best.Count];
        for (var i = hits.Length - 1; i >= 0; i--)
        {
            var candidate = best.Dequeue();
            hits[i] = candidate.Chunk.ToHit(candidate.Score);
        }

        return Task.FromResult<IReadOnlyList<VectorSearchHit>>(hits);
    }

    public Task<IReadOnlyList<VectorSearchHit>> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeKeywordSearch(query);
        if (search.MatchesNothing)
        {
            return Task.FromResult(NoHits);
        }

        var patterns = search.Patterns.Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant)).ToArray();
        var chunks = _snapshot.BySource.Values
            .SelectMany(bucket => bucket)
            .Where(chunk => string.Equals(chunk.EmbeddingModel, search.EmbeddingModel, StringComparison.Ordinal))
            .ToList();
        if (chunks.Count == 0)
        {
            return Task.FromResult(NoHits);
        }

        // Statistics cover every chunk of the model regardless of the source filter, so scores do not depend on it.
        var termFrequencies = new int[chunks.Count][];
        var documentFrequencies = new long[patterns.Length];
        long totalLength = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLength += chunks[i].Length;
            termFrequencies[i] = new int[patterns.Length];
            for (var term = 0; term < patterns.Length; term++)
            {
                var count = patterns[term].Count(chunks[i].SearchText);
                termFrequencies[i][term] = count;
                if (count > 0)
                {
                    documentFrequencies[term]++;
                }
            }
        }

        var averageLength = (double)totalLength / chunks.Count;
        var idf = documentFrequencies.Select(df => KeywordScoring.Idf(chunks.Count, df)).ToArray();
        var sourceFilter = search.SourceIds?.ToHashSet();
        IReadOnlyList<VectorSearchHit> hits = chunks
            .Select((chunk, index) => (Chunk: chunk, Score: Score(termFrequencies[index], chunk.Length)))
            .Where(candidate => candidate.Score > 0 && (sourceFilter is null || sourceFilter.Contains(candidate.Chunk.SourceId)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Chunk.SourceId)
            .ThenBy(candidate => candidate.Chunk.ChunkIndex)
            .Take(search.TopK)
            .Select(candidate => candidate.Chunk.ToHit(candidate.Score))
            .ToArray();
        return Task.FromResult(hits);

        double Score(int[] frequencies, int length)
        {
            var score = 0d;
            for (var term = 0; term < frequencies.Length; term++)
            {
                score += KeywordScoring.TermScore(idf[term], frequencies[term], length, averageLength);
            }

            return score;
        }
    }

    public Task<TextSearchPage<VectorSearchHit>> FindTextAsync(TextSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeTextSearch(query);
        if (search.MatchesNothing)
        {
            return Task.FromResult(TextSearchPage<VectorSearchHit>.Empty);
        }

        var matches = MatchText(search, cancellationToken);
        IReadOnlyList<VectorSearchHit> items = matches
            .Skip(search.Skip)
            .Take(search.Take)
            .Select(chunk => chunk.ToHit(0d))
            .ToArray();
        var sourceCount = matches.Select(chunk => chunk.SourceId).Distinct().Count();
        return Task.FromResult(new TextSearchPage<VectorSearchHit>(items, matches.Count, sourceCount));
    }

    public Task<TextSearchPage<SourceTextMatches>> CountTextBySourceAsync(TextSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = VectorStoreGuard.NormalizeTextSearch(query);
        if (search.MatchesNothing)
        {
            return Task.FromResult(TextSearchPage<SourceTextMatches>.Empty);
        }

        var matches = MatchText(search, cancellationToken);
        var bySource = matches
            .GroupBy(chunk => chunk.SourceId)
            .Select(group => new SourceTextMatches(group.Key, group.Count()))
            .OrderByDescending(source => source.Chunks)
            .ThenBy(source => source.SourceId)
            .ToList();
        IReadOnlyList<SourceTextMatches> items = bySource.Skip(search.Skip).Take(search.Take).ToArray();
        return Task.FromResult(new TextSearchPage<SourceTextMatches>(items, matches.Count, bySource.Count));
    }

    public Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (offset, limit) = VectorStoreGuard.NormalizePage(skip, take);
        if (limit == 0 || !_snapshot.BySource.TryGetValue(sourceId, out var bucket))
        {
            return Task.FromResult(NoHits);
        }

        IReadOnlyList<VectorSearchHit> hits = bucket
            .OrderBy(chunk => chunk.ChunkIndex)
            .ThenBy(chunk => chunk.EmbeddingModel, StringComparer.Ordinal)
            .Skip(offset)
            .Take(limit)
            .Select(chunk => chunk.ToHit(0d))
            .ToArray();
        return Task.FromResult(hits);
    }

    public Task<long> CountAsync(Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _snapshot;
        if (sourceId is not Guid id)
        {
            return Task.FromResult(snapshot.Count);
        }

        return Task.FromResult(snapshot.BySource.TryGetValue(id, out var bucket) ? bucket.Length : 0L);
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    // Source and chunk order; a chunk stored for several models is kept once, like the ClickHouse query.
    private List<StoredChunk> MatchText(NormalizedTextSearch search, CancellationToken cancellationToken)
    {
        var pattern = new Regex(search.Pattern, RegexOptions.CultureInvariant);
        var matches = new Dictionary<(Guid SourceId, int ChunkIndex), StoredChunk>();
        foreach (var bucket in SelectBuckets(_snapshot, search.SourceIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var chunk in bucket)
            {
                var key = (chunk.SourceId, chunk.ChunkIndex);
                if (pattern.IsMatch(chunk.Content)
                    && (!matches.TryGetValue(key, out var kept) || string.CompareOrdinal(chunk.EmbeddingModel, kept.EmbeddingModel) < 0))
                {
                    matches[key] = chunk;
                }
            }
        }

        return matches.Values.OrderBy(chunk => chunk.SourceId).ThenBy(chunk => chunk.ChunkIndex).ToList();
    }

    private static IEnumerable<ImmutableArray<StoredChunk>> SelectBuckets(Snapshot snapshot, Guid[]? sourceIds)
    {
        if (sourceIds is null)
        {
            return snapshot.BySource.Values;
        }

        return sourceIds
            .Select(id => snapshot.BySource.TryGetValue(id, out var bucket) ? bucket : ImmutableArray<StoredChunk>.Empty)
            .Where(bucket => !bucket.IsEmpty);
    }

    private sealed record Snapshot(ImmutableDictionary<Guid, ImmutableArray<StoredChunk>> BySource, long Count)
    {
        public static Snapshot Empty { get; } = new(ImmutableDictionary<Guid, ImmutableArray<StoredChunk>>.Empty, 0);
    }

    private sealed class StoredChunk(
        Guid chunkId,
        Guid sourceId,
        int chunkIndex,
        string content,
        string embeddingModel,
        float[] embedding,
        IReadOnlyDictionary<string, string> metadata,
        string context)
    {
        public Guid SourceId { get; } = sourceId;
        public int ChunkIndex { get; } = chunkIndex;
        public string Content { get; } = content;

        public string SearchText { get; } = context.Length == 0 ? content : context + " " + content;
        public int Length { get; } = KeywordScoring.Length(context.Length == 0 ? content : context + " " + content);
        public string EmbeddingModel { get; } = embeddingModel;
        public float[] Embedding { get; } = embedding;
        public double SumOfSquares { get; } = VectorMath.SumOfSquares(embedding);

        public static StoredChunk From(ChunkRecord chunk) => new(
            chunk.ChunkId,
            chunk.SourceId,
            chunk.ChunkIndex,
            chunk.Content,
            chunk.EmbeddingModel,
            chunk.Embedding.ToArray(),
            ChunkMetadataSerializer.Copy(chunk.Metadata),
            chunk.Context);

        public VectorSearchHit ToHit(double score) => new(chunkId, SourceId, ChunkIndex, Content, score, metadata);
    }

    private readonly record struct Candidate(StoredChunk Chunk, double Score);

    // Worst first. Ties are broken by source id, then chunk index, like the ClickHouse query.
    private sealed class WorstFirstComparer : IComparer<Candidate>
    {
        public static WorstFirstComparer Instance { get; } = new();

        public int Compare(Candidate x, Candidate y)
        {
            var byScore = x.Score.CompareTo(y.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var bySource = y.Chunk.SourceId.CompareTo(x.Chunk.SourceId);
            return bySource != 0 ? bySource : y.Chunk.ChunkIndex.CompareTo(x.Chunk.ChunkIndex);
        }
    }
}
