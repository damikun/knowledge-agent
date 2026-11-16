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

    public int EmbeddedChunks { get; set; }

    public string? JobRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Guid? FolderId { get; set; }

    public Guid? ParentId { get; set; }

    public WebsiteCrawlSettings? Crawl { get; set; }

    public int PagesFound { get; set; }
    public int PagesDone { get; set; }
    public int PagesFailed { get; set; }

    public DateTimeOffset? LastFetchedAt { get; set; }
}

public sealed class SourceContent
{
    public Guid SourceId { get; set; }
    public byte[]? RawBytes { get; set; }
    public string? ExtractedText { get; set; }
}
