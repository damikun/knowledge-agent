namespace KnowledgeAgent.Features.Sources;

public sealed record SourceDto(
    Guid Id,
    string Type,
    string Status,
    string Title,
    string? Url,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    int ChunkCount,
    int? PageCount,
    int TokenEstimate,
    string? EmbeddingModel,
    string? Error,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProcessedAt,
    IReadOnlyDictionary<string, string> Metadata,
    Guid? FolderId,
    string? Site,
    string? Stage,
    int EmbeddedChunks,
    string? JobRunId,
    string? JobRunUrl)
{
    public Guid? ParentId { get; init; }

    public WebsiteCrawlDto? Crawl { get; init; }

    public DateTimeOffset? LastFetchedAt { get; init; }
}

// A website's settings and the progress of its current or last crawl.
public sealed record WebsiteCrawlDto(string Scope, int MaxPages, IReadOnlyList<string> Exclude, int PagesFound, int PagesDone, int PagesFailed);

public sealed record SourceSiteDto(string Site, int Count);

public sealed record SourcePageDto(IReadOnlyList<SourceDto> Items, int Total, int Offset, int Limit, int? NextOffset);

public sealed record SourceStatsDto(
    int Total,
    int Ready,
    int Pending,
    int Processing,
    int Failed,
    long ReadyChunks,
    IReadOnlyDictionary<string, int> ReadyByType,
    IReadOnlyDictionary<string, int> ReadyByEmbeddingModel,
    int Waiting);

// Scope: page (the default), section or site. MaxPages and Exclude apply to section and site.
public sealed record AddUrlSourceRequest(
    string Url,
    string? Title = null,
    Guid? FolderId = null,
    string? Scope = null,
    int? MaxPages = null,
    IReadOnlyList<string>? Exclude = null);

public sealed record AddTextSourceRequest(string Title, string Content, string? Format = null, Guid? FolderId = null);

public sealed record ChunkDto(Guid ChunkId, int ChunkIndex, string Content, int? Page, IReadOnlyDictionary<string, string> Metadata);

public sealed record ReindexResponse(int Queued);
