using KnowledgeAgent.Features.Sources.Crawling;

namespace KnowledgeAgent.Features.Sources;

public sealed class Source
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public SourceType Type { get; set; }
    public SourceStatus Status { get; set; } = SourceStatus.Pending;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Site { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public int ChunkCount { get; set; }
    public int? PageCount { get; set; }
    public int TokenEstimate { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public SourceStage? Stage { get; set; }

    // Progress of the current or last attempt. While embedding or storing, ChunkCount is that attempt's total.
    public int EmbeddedChunks { get; set; }

    // The job engine's run that last processed the source, so the portal can link to it.
    public string? JobRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Guid? FolderId { get; set; }

    // The website a crawled page belongs to.
    public Guid? ParentId { get; set; }

    // Set on websites only.
    public WebsiteCrawlSettings? Crawl { get; set; }

    // Progress of a website's current or last crawl.
    public int PagesFound { get; set; }
    public int PagesDone { get; set; }
    public int PagesFailed { get; set; }

    // When the crawler last fetched or revalidated a page.
    public DateTimeOffset? LastFetchedAt { get; set; }
}

// Kept in a separate 1:1 table so listings do not load the large payloads.
public sealed class SourceContent
{
    public Guid SourceId { get; set; }
    public byte[]? RawBytes { get; set; }
    public string? ExtractedText { get; set; }
}
