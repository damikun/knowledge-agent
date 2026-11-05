using System.Globalization;

namespace KnowledgeAgent.Infrastructure.VectorStore;

public static class ChunkMetadataKeys
{
    public const string Title = "title";
    public const string Url = "url";
    public const string Page = "page";
    public const string SourceType = "sourceType";

    public static int? ParsePage(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(Page, out var page)
        && int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

// Keyword search reads the context with the content; results and citations show only the content.
public sealed record ChunkRecord(
    Guid ChunkId,
    Guid SourceId,
    int ChunkIndex,
    string Content,
    string EmbeddingModel,
    ReadOnlyMemory<float> Embedding,
    IReadOnlyDictionary<string, string> Metadata,
    string Context = "");

public sealed record VectorSearchQuery(
    ReadOnlyMemory<float> Vector,
    string EmbeddingModel,
    int TopK,
    IReadOnlyCollection<Guid>? SourceIds = null,
    double? MinScore = null);

public sealed record KeywordSearchQuery(
    IReadOnlyList<string> Terms,
    string EmbeddingModel,
    int TopK,
    IReadOnlyCollection<Guid>? SourceIds = null);

public sealed record VectorSearchHit(
    Guid ChunkId,
    Guid SourceId,
    int ChunkIndex,
    string Content,
    double Score,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record TextSearchQuery(
    string Text,
    bool MatchCase = false,
    bool WholeWords = true,
    IReadOnlyCollection<Guid>? SourceIds = null,
    int Skip = 0,
    int Take = 20);

public sealed record TextSearchPage<T>(IReadOnlyList<T> Items, long TotalChunks, long TotalSources)
{
    public static TextSearchPage<T> Empty { get; } = new([], 0, 0);
}

public sealed record SourceTextMatches(Guid SourceId, long Chunks);
