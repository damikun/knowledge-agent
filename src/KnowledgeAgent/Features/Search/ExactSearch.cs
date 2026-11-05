using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;

namespace KnowledgeAgent.Features.Search;

public interface IExactSearch
{
    Task<ExactSearchPage<KnowledgeSearchHit>> FindPassagesAsync(ExactSearchRequest request, CancellationToken cancellationToken = default);

    Task<ExactSearchPage<ExactSearchSource>> FindSourcesAsync(ExactSearchRequest request, CancellationToken cancellationToken = default);
}

public sealed record ExactSearchRequest(
    string Text,
    bool MatchCase = false,
    bool WholeWords = true,
    IReadOnlyCollection<Guid>? SourceIds = null,
    int Offset = 0,
    int Limit = 10);

public sealed record ExactSearchPage<T>(IReadOnlyList<T> Items, int Offset, int? NextOffset, long TotalPassages, long TotalSources);

public sealed record ExactSearchSource(Guid SourceId, string Title, string? Url, long Passages);

// Literal matching: ranks nothing and needs no query embedding, so it also finds chunks embedded with another model.
internal sealed class ExactSearch(IVectorStore vectorStore, ISourceRepository repository) : IExactSearch
{
    public const int MaxLimit = 100;

    // Exact matches have no similarity to report, so every hit gets the top score.
    private const double ExactScore = 1d;

    public async Task<ExactSearchPage<KnowledgeSearchHit>> FindPassagesAsync(ExactSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = ToQuery(request);
        var page = await vectorStore.FindTextAsync(query, cancellationToken).ConfigureAwait(false);
        var sources = await LookupSourcesAsync(page.Items.Select(hit => hit.SourceId), cancellationToken).ConfigureAwait(false);
        var hits = page.Items
            .Where(hit => sources.ContainsKey(hit.SourceId))
            .Select(hit => ToHit(hit, sources[hit.SourceId]))
            .ToList();
        return new ExactSearchPage<KnowledgeSearchHit>(hits, query.Skip, NextOffset(query, page.Items.Count, page.TotalChunks), page.TotalChunks, page.TotalSources);
    }

    public async Task<ExactSearchPage<ExactSearchSource>> FindSourcesAsync(ExactSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = ToQuery(request);
        var page = await vectorStore.CountTextBySourceAsync(query, cancellationToken).ConfigureAwait(false);
        var sources = await LookupSourcesAsync(page.Items.Select(match => match.SourceId), cancellationToken).ConfigureAwait(false);
        var items = page.Items
            .Where(match => sources.ContainsKey(match.SourceId))
            .Select(match =>
            {
                var source = sources[match.SourceId];
                return new ExactSearchSource(
                    source.Id,
                    string.IsNullOrWhiteSpace(source.Title) ? KnowledgeSearch.UntitledSource : source.Title,
                    string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                    match.Chunks);
            })
            .ToList();
        return new ExactSearchPage<ExactSearchSource>(items, query.Skip, NextOffset(query, page.Items.Count, page.TotalSources), page.TotalChunks, page.TotalSources);
    }

    private static TextSearchQuery ToQuery(ExactSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = TextMatching.Clean(request.Text);
        if (text.Length == 0)
        {
            throw new ValidationFailedException("text", "The text to find is required.");
        }

        if (text.Length > TextMatching.MaxTextLength)
        {
            throw new ValidationFailedException("text", $"The text to find must be at most {TextMatching.MaxTextLength} characters.");
        }

        return new TextSearchQuery(
            text,
            request.MatchCase,
            request.WholeWords,
            request.SourceIds is { Count: > 0 } ids ? ids : null,
            Math.Max(0, request.Offset),
            Math.Clamp(request.Limit, 1, MaxLimit));
    }

    // Counts the rows the store returned, so a page that lost hits of deleted sources still continues after them.
    private static int? NextOffset(TextSearchQuery query, int returned, long total) =>
        query.Skip + returned < total ? query.Skip + returned : null;

    private static KnowledgeSearchHit ToHit(VectorSearchHit chunk, Source source) => new(
        chunk.ChunkId,
        chunk.SourceId,
        chunk.ChunkIndex,
        Metadata(chunk, ChunkMetadataKeys.Title) ?? NullIfEmpty(source.Title) ?? KnowledgeSearch.UntitledSource,
        Metadata(chunk, ChunkMetadataKeys.Url) ?? NullIfEmpty(source.Url),
        ChunkMetadataKeys.ParsePage(chunk.Metadata),
        chunk.Content,
        ExactScore);

    private async Task<Dictionary<Guid, Source>> LookupSourcesAsync(IEnumerable<Guid> sourceIds, CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, Source>();
        foreach (var batch in sourceIds.Distinct().Chunk(SourceQuery.MaxLimit))
        {
            var page = await repository.FindAsync(new SourceQuery(Ids: batch, Limit: batch.Length), cancellationToken).ConfigureAwait(false);
            foreach (var source in page.Items)
            {
                found[source.Id] = source;
            }
        }

        return found;
    }

    private static string? Metadata(VectorSearchHit hit, string key) =>
        hit.Metadata.TryGetValue(key, out var value) ? NullIfEmpty(value) : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
