using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Features.Sources;

public static class SourceMappings
{
    public static SourceDto ToDto(this Source s, JobEngineInfo jobs) => new(
        s.Id,
        s.Type.ToString().ToLowerInvariant(),
        s.Status.ToString().ToLowerInvariant(),
        s.Title,
        s.Url,
        s.FileName,
        s.ContentType,
        s.SizeBytes,
        s.ChunkCount,
        s.PageCount,
        s.TokenEstimate,
        s.EmbeddingModel,
        s.Error,
        s.Attempts,
        s.CreatedAt,
        s.UpdatedAt,
        s.ProcessedAt,
        s.Metadata,
        s.FolderId,
        s.Site,
        s.Stage?.ToString().ToLowerInvariant(),
        s.EmbeddedChunks,
        s.JobRunId,
        jobs.RunUrl(s.JobRunId))
    {
        ParentId = s.ParentId,
        LastFetchedAt = s.LastFetchedAt,
        Crawl = s.Crawl is { } crawl
            ? new WebsiteCrawlDto(crawl.Scope.ToString().ToLowerInvariant(), crawl.MaxPages, crawl.Exclude, s.PagesFound, s.PagesDone, s.PagesFailed)
            : null,
    };

    public static SourcePageDto ToDto(this SourcePage page, int limit, JobEngineInfo jobs) => new(
        page.Items.Select(source => source.ToDto(jobs)).ToList(),
        page.Total,
        page.Offset,
        limit,
        page.NextOffset);

    public static SourceStatsDto ToDto(this SourceStats stats) => new(
        stats.CountByStatus.Values.Sum(),
        stats.Count(SourceStatus.Ready),
        stats.Count(SourceStatus.Pending),
        stats.Count(SourceStatus.Processing),
        stats.Count(SourceStatus.Failed),
        stats.ReadyChunkCount,
        stats.ReadyCountByType.ToDictionary(entry => entry.Key.ToString().ToLowerInvariant(), entry => entry.Value),
        new Dictionary<string, int>(stats.ReadyCountByEmbeddingModel),
        stats.Count(SourceStatus.Waiting));

    public static ChunkDto ToChunkDto(this VectorSearchHit hit) => new(
        hit.ChunkId,
        hit.ChunkIndex,
        hit.Content,
        ChunkMetadataKeys.ParsePage(hit.Metadata),
        hit.Metadata);
}
