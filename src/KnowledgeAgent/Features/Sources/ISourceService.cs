using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Infrastructure.VectorStore;

namespace KnowledgeAgent.Features.Sources;

public interface ISourceService
{
    Task<Source> AddUrlAsync(string url, string? title = null, Guid? folderId = null, CancellationToken cancellationToken = default);

    // Scope Page adds a single page; the other scopes add a website whose pages are crawled.
    Task<Source> AddWebsiteAsync(string url, WebsiteCrawlSettings settings, string? title = null, Guid? folderId = null, CancellationToken cancellationToken = default);

    Task<Source> AddFileAsync(
        string fileName,
        string? contentType,
        byte[] content,
        string? title = null,
        Guid? folderId = null,
        CancellationToken cancellationToken = default);

    Task<Source> AddTextAsync(
        string title,
        string content,
        SourceType type = SourceType.Text,
        Guid? folderId = null,
        CancellationToken cancellationToken = default);

    Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, CancellationToken cancellationToken = default);
    Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Source?> ReingestAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> ReingestAllAsync(CancellationToken cancellationToken = default);
    Task<int> QueueWaitingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid id, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
}
