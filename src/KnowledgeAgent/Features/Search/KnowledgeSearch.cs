using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Search;

internal sealed partial class KnowledgeSearch(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ISourceRepository repository,
    IOptionsMonitor<SearchOptions> options,
    ILogger<KnowledgeSearch> logger,
    IRerankService? reranker = null) : IKnowledgeSearch
{
    public const int MaxTopK = 50;
    public const int MaxCandidatePool = 100;
    public const string UntitledSource = "Untitled source";

    internal const int MaxRerankDocumentChars = 4_000;

    public async Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ValidationFailedException("query", "A search query is required.");
        }

        var settings = options.CurrentValue;
        var query = request.Query.Trim();
        var topK = Math.Clamp(request.TopK, 1, MaxTopK);
        var mode = SearchModes.Normalize(request.Mode) ?? SearchModes.Normalize(settings.Mode) ?? SearchModes.Hybrid;
        var pool = Math.Clamp(Math.Max(topK, settings.CandidatePool), 1, MaxCandidatePool);
        var sourceIds = request.SourceIds is { Count: > 0 } ids ? ids : null;

        using var activity = SearchTelemetry.ActivitySource.StartActivity("search knowledge");
        activity?.SetTag(SearchTelemetry.Tags.TopK, topK);
        activity?.SetTag(SearchTelemetry.Tags.SearchMode, mode);

        var embedding = await embeddingService.EmbedAsync([query], EmbeddingPurpose.Query, cancellationToken).ConfigureAwait(false);
        if (embedding.Vectors.Count == 0)
        {
            throw new InvalidOperationException("The embedding service returned no vector for the query.");
        }

        activity?.SetTag(SearchTelemetry.Tags.EmbeddingModel, embedding.ModelKey);
        var terms = mode == SearchModes.Vector ? [] : QueryTermAnalyzer.Terms(query);
        activity?.SetTag(SearchTelemetry.Tags.KeywordTerms, terms.Count);

        var vectorTask = mode == SearchModes.Keyword
            ? Task.FromResult<IReadOnlyList<VectorSearchHit>>([])
            : vectorStore.SearchAsync(new VectorSearchQuery(embedding.Vectors[0], embedding.ModelKey, pool, sourceIds, request.MinScore), cancellationToken);

        // Keyword search also filters by the embedding model key, so both retrievers search the same chunk set.
        var keywordTask = terms.Count == 0
            ? Task.FromResult(KeywordResult.Skipped)
            : KeywordSearchAsync(new KeywordSearchQuery(terms, embedding.ModelKey, pool, sourceIds), cancellationToken);
        var vectorHits = await vectorTask.ConfigureAwait(false);
        var keyword = await keywordTask.ConfigureAwait(false);
        activity?.SetTag(SearchTelemetry.Tags.VectorHits, vectorHits.Count);
        activity?.SetTag(SearchTelemetry.Tags.KeywordHits, keyword.Hits.Count);

        var degraded = new List<string>();
        if (keyword.Outcome == KeywordOutcome.Failed)
        {
            degraded.Add(SearchDegradations.KeywordFailed);
        }

        // Scores are relative to the retrievers that returned something, so a failed or empty keyword search
        // leaves the best vector hit at 1 instead of halving every score.
        var candidates = mode switch
        {
            SearchModes.Vector => RankFusion.Single(vectorHits, keyword: false),
            SearchModes.Keyword => RankFusion.Single(keyword.Hits, keyword: true),
            _ => RankFusion.Fuse(vectorHits, keyword.Hits, settings.RrfK, keywordRan: keyword.Hits.Count > 0, settings.VectorWeight, settings.KeywordWeight),
        };

        // Drop hits of deleted sources before reranking, so they take no reranker slots.
        var (sources, lookupFailed) = await LookupSourcesAsync(candidates.Select(candidate => candidate.Chunk.SourceId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        if (lookupFailed)
        {
            degraded.Add(SearchDegradations.SourceLookupFailed);
        }

        var live = candidates
            .Where(candidate => !sources.TryGetValue(candidate.Chunk.SourceId, out var lookup) || !lookup.IsDeleted)
            .Select(candidate => ToHit(candidate, sources.GetValueOrDefault(candidate.Chunk.SourceId)))
            .ToList();

        IReadOnlyList<KnowledgeSearchHit> results = live;
        if (request.Rerank ?? settings.Rerank)
        {
            var (reranked, rerankFailed) = await RerankAsync(query, live, Math.Clamp(settings.RerankCandidates, 1, MaxCandidatePool), activity, cancellationToken).ConfigureAwait(false);
            results = reranked;
            if (rerankFailed)
            {
                degraded.Add(SearchDegradations.RerankFailed);
            }
        }

        var hits = results.Take(topK).ToList();
        activity?.SetTag(SearchTelemetry.Tags.HitCount, hits.Count);
        SearchTelemetry.RecordDegraded(activity, degraded);
        return new KnowledgeSearchResult(hits, degraded);
    }

    private async Task<KeywordResult> KeywordSearchAsync(KeywordSearchQuery query, CancellationToken cancellationToken)
    {
        try
        {
            return new KeywordResult(await vectorStore.KeywordSearchAsync(query, cancellationToken).ConfigureAwait(false), KeywordOutcome.Succeeded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogKeywordSearchFailed(logger, ex);
            return new KeywordResult([], KeywordOutcome.Failed);
        }
    }

    private async Task<(IReadOnlyList<KnowledgeSearchHit> Hits, bool Failed)> RerankAsync(
        string query,
        List<KnowledgeSearchHit> hits,
        int candidates,
        System.Diagnostics.Activity? activity,
        CancellationToken cancellationToken)
    {
        if (reranker is null || hits.Count < 2)
        {
            return (hits, false);
        }

        var head = hits.Take(candidates).ToList();
        RerankResult? result;
        try
        {
            result = await reranker.RerankAsync(query, head.Select(RerankDocument).ToList(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRerankFailed(logger, ex);
            return (hits, true);
        }

        // Null means no reranker is configured; a score count that doesn't match is a broken answer.
        if (result is null)
        {
            return (hits, false);
        }

        if (result.Scores.Count != head.Count)
        {
            return (hits, true);
        }

        activity?.SetTag(SearchTelemetry.Tags.Reranker, result.Model);

        // Ties keep the fused order, which matters because coarse LLM ratings tie often.
        var reranked = head
            .Select((hit, index) => (Hit: hit, Relevance: Math.Clamp(result.Scores[index], 0d, 1d), Index: index))
            .OrderByDescending(entry => entry.Relevance)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Hit with { Score = entry.Relevance, RerankScore = entry.Relevance })
            .ToList();
        reranked.AddRange(hits.Skip(head.Count));
        return (reranked, false);
    }

    private static string RerankDocument(KnowledgeSearchHit hit)
    {
        var text = hit.SourceTitle + "\n\n" + hit.Content.Trim();
        return text.Length <= MaxRerankDocumentChars ? text : text[..MaxRerankDocumentChars];
    }

    private static KnowledgeSearchHit ToHit(FusedCandidate candidate, SourceLookup lookup)
    {
        var chunk = candidate.Chunk;
        return new KnowledgeSearchHit(
            chunk.ChunkId,
            chunk.SourceId,
            chunk.ChunkIndex,
            Metadata(chunk, ChunkMetadataKeys.Title) ?? NullIfEmpty(lookup.Source?.Title) ?? UntitledSource,
            Metadata(chunk, ChunkMetadataKeys.Url) ?? NullIfEmpty(lookup.Source?.Url),
            ChunkMetadataKeys.ParsePage(chunk.Metadata),
            chunk.Content,
            candidate.Score)
        {
            VectorScore = candidate.VectorScore,
            VectorRank = candidate.VectorRank,
            KeywordScore = candidate.KeywordScore,
            KeywordRank = candidate.KeywordRank,
        };
    }

    private async Task<(Dictionary<Guid, SourceLookup> Lookups, bool Failed)> LookupSourcesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken)
    {
        var lookups = new Dictionary<Guid, SourceLookup>();
        if (sourceIds.Count == 0)
        {
            return (lookups, false);
        }

        try
        {
            var found = new Dictionary<Guid, Source>();
            foreach (var batch in sourceIds.Chunk(SourceQuery.MaxLimit))
            {
                var page = await repository.FindAsync(new SourceQuery(Ids: batch, Limit: batch.Length), cancellationToken).ConfigureAwait(false);
                foreach (var source in page.Items)
                {
                    found[source.Id] = source;
                }
            }

            foreach (var id in sourceIds)
            {
                lookups[id] = found.TryGetValue(id, out var source) ? new SourceLookup(source, IsDeleted: false) : SourceLookup.Deleted;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourceLookupFailed(logger, ex, sourceIds.Count);
            lookups.Clear();
            return (lookups, true);
        }

        return (lookups, false);
    }

    private static string? Metadata(VectorSearchHit hit, string key) =>
        hit.Metadata.TryGetValue(key, out var value) ? NullIfEmpty(value) : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load {Count} source(s) for search hits; using chunk metadata only")]
    private static partial void LogSourceLookupFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keyword search failed; using vector results only")]
    private static partial void LogKeywordSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reranking failed; keeping the fused order")]
    private static partial void LogRerankFailed(ILogger logger, Exception exception);

    private enum KeywordOutcome
    {
        Skipped,
        Succeeded,
        Failed,
    }

    private readonly record struct KeywordResult(IReadOnlyList<VectorSearchHit> Hits, KeywordOutcome Outcome)
    {
        public static KeywordResult Skipped => new([], KeywordOutcome.Skipped);
    }

    private readonly record struct SourceLookup(Source? Source, bool IsDeleted)
    {
        public static SourceLookup Deleted => new(null, true);
    }
}
