namespace KnowledgeAgent.Features.Sources;

public interface ISourceRepository
{
    Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default);
    Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListByStatusAsync(IReadOnlyCollection<SourceStatus> statuses, CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<Guid>> ListIdsInFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Where(source => source.FolderId is { } folderId && folderIds.Contains(folderId)).Select(source => source.Id).ToList();

    async Task<IReadOnlyList<Guid>> ListIdsOutsideFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Where(source => source.FolderId is not { } folderId || !folderIds.Contains(folderId)).Select(source => source.Id).ToList();

    Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default);

    Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Source source, CancellationToken cancellationToken = default);

    Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default);

    Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default);

    Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task SaveContentAsync(Guid sourceId, string? extractedText, byte[]? rawBytes = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<Source>> ListPagesAsync(Guid websiteId, CancellationToken cancellationToken = default) =>
        [.. (await ListAsync(cancellationToken)).Where(source => source.ParentId == websiteId).OrderBy(source => source.CreatedAt).ThenBy(source => source.Id)];

    async Task<Source?> FindPageAsync(Guid websiteId, string url, CancellationToken cancellationToken = default) =>
        (await ListPagesAsync(websiteId, cancellationToken)).FirstOrDefault(source => string.Equals(source.Url, url, StringComparison.Ordinal));

    async Task UpdateCrawlProgressAsync(Guid websiteId, int found, int done, int failed, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(websiteId, cancellationToken) is { } website)
        {
            website.PagesFound = Math.Max(website.PagesFound, found);
            website.PagesDone += done;
            website.PagesFailed += failed;
            await UpdateAsync(website, cancellationToken);
        }
    }
}
