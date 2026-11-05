namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed record NormalizedSearch(
    ReadOnlyMemory<float> Vector,
    string EmbeddingModel,
    int TopK,
    Guid[]? SourceIds,
    double? MinScore,
    bool MatchesNothing);

internal static class VectorStoreGuard
{
    public static void ValidateChunks(IReadOnlyList<ChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i]
                ?? throw new ArgumentException($"Chunk at position {i} is null.", nameof(chunks));

            if (chunk.ChunkIndex < 0)
            {
                throw new ArgumentException($"Chunk at position {i} has a negative chunk index ({chunk.ChunkIndex}).", nameof(chunks));
            }

            if (string.IsNullOrWhiteSpace(chunk.EmbeddingModel))
            {
                throw new ArgumentException($"Chunk at position {i} has no embedding model.", nameof(chunks));
            }

            if (chunk.Content is null)
            {
                throw new ArgumentException($"Chunk at position {i} has no content.", nameof(chunks));
            }

            if (chunk.Embedding.Length is 0 or > VectorStoreLimits.MaxDimensions)
            {
                throw new ArgumentException(
                    $"Chunk at position {i} has {chunk.Embedding.Length} embedding dimensions; expected 1..{VectorStoreLimits.MaxDimensions}.",
                    nameof(chunks));
            }
        }
    }

    public static NormalizedSearch NormalizeSearch(VectorSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Vector.Length is 0 or > VectorStoreLimits.MaxDimensions)
        {
            throw new ArgumentException(
                $"The query vector has {query.Vector.Length} dimensions; expected 1..{VectorStoreLimits.MaxDimensions}.",
                nameof(query));
        }

        if (string.IsNullOrWhiteSpace(query.EmbeddingModel))
        {
            throw new ArgumentException("The query has no embedding model.", nameof(query));
        }

        double? minScore = query.MinScore;
        if (minScore is double min && double.IsNaN(min))
        {
            throw new ArgumentException("MinScore must be a number.", nameof(query));
        }

        // Scores are clamped to [-1, 1], so a threshold at or below -1 filters nothing and one above 1 filters everything.
        var matchesNothing = minScore > 1d;
        if (minScore <= -1d)
        {
            minScore = null;
        }

        Guid[]? sourceIds = query.SourceIds is { Count: > 0 } ids ? ids.Distinct().ToArray() : null;

        return new NormalizedSearch(
            query.Vector,
            query.EmbeddingModel,
            Math.Clamp(query.TopK, 1, VectorStoreLimits.MaxTopK),
            sourceIds,
            minScore,
            matchesNothing);
    }

    public static NormalizedKeywordSearch NormalizeKeywordSearch(KeywordSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.EmbeddingModel))
        {
            throw new ArgumentException("The query has no embedding model.", nameof(query));
        }

        var terms = (query.Terms ?? [])
            .Select(term => term?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(term => term.Length > 0)
            .Select(term => term.Length > KeywordScoring.MaxTermLength ? term[..KeywordScoring.MaxTermLength] : term)
            .Distinct(StringComparer.Ordinal)
            .Take(KeywordScoring.MaxTerms)
            .ToList();

        return new NormalizedKeywordSearch(
            terms,
            terms.Select(KeywordScoring.Pattern).ToList(),
            query.EmbeddingModel,
            Math.Clamp(query.TopK, 1, VectorStoreLimits.MaxTopK),
            query.SourceIds is { Count: > 0 } ids ? ids.Distinct().ToArray() : null);
    }

    public static NormalizedTextSearch NormalizeTextSearch(TextSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = TextMatching.Clean(query.Text);
        if (text.Length > TextMatching.MaxTextLength)
        {
            throw new ArgumentException(
                $"The text to find has {text.Length} characters; at most {TextMatching.MaxTextLength} are allowed.",
                nameof(query));
        }

        var (skip, take) = NormalizePage(query.Skip, query.Take);
        return new NormalizedTextSearch(
            TextMatching.Pattern(text, query.MatchCase, query.WholeWords),
            TextMatching.Needle(text),
            query.MatchCase,
            query.SourceIds is { Count: > 0 } ids ? ids.Distinct().ToArray() : null,
            skip,
            take);
    }

    public static (int Skip, int Take) NormalizePage(int skip, int take) =>
        (Math.Max(0, skip), Math.Clamp(take, 0, VectorStoreLimits.MaxPageSize));

    // Non-finite scores become 0 because NaN and infinity cannot be serialized to JSON.
    public static double NormalizeScore(double cosineSimilarity) =>
        double.IsFinite(cosineSimilarity) ? Math.Clamp(cosineSimilarity, -1d, 1d) : 0d;
}
