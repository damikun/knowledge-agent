namespace KnowledgeAgent.Features.Sources;

public interface ISourceRepository
{
    Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default);
    Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<Source>> ListByStatusAsync(IReadOnlyCollection<SourceStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>The sources filed directly in one of the folders.</summary>
    async Task<IReadOnlyList<Guid>> ListIdsInFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Where(source => source.FolderId is { } folderId && folderIds.Contains(folderId)).Select(source => source.Id).ToList();

    /// <summary>Every source except those filed directly in one of the folders; unfiled sources included.</summary>
    async Task<IReadOnlyList<Guid>> ListIdsOutsideFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken)).Where(source => source.FolderId is not { } folderId || !folderIds.Contains(folderId)).Select(source => source.Id).ToList();

    /// <summary>One page of matching sources. Every text term must match the title, URL or file name.</summary>
    Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default);

    Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default);

    // Leaves the folder unchanged; only MoveToFolderAsync changes it.
    Task UpdateAsync(Source source, CancellationToken cancellationToken = default);

    // Returns how many of the ids exist; unknown ids are skipped.
    Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default);

    // Moves every source the query's filters match, ignoring its paging, and returns how many there were.
    Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default);

    // Sites of the sources the query's filters match, the most frequent first.
    Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default);

    Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the content row. Null arguments keep the stored values.</summary>
    Task SaveContentAsync(Guid sourceId, string? extractedText, byte[]? rawBytes = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The pages of a website, oldest first.</summary>
    async Task<IReadOnlyList<Source>> ListPagesAsync(Guid websiteId, CancellationToken cancellationToken = default) =>
        [.. (await ListAsync(cancellationToken)).Where(source => source.ParentId == websiteId).OrderBy(source => source.CreatedAt).ThenBy(source => source.Id)];

    async Task<Source?> FindPageAsync(Guid websiteId, string url, CancellationToken cancellationToken = default) =>
        (await ListPagesAsync(websiteId, cancellationToken)).FirstOrDefault(source => string.Equals(source.Url, url, StringComparison.Ordinal));

    // Sets the pages found and adds to the done and failed counts in one write, so it never loses a concurrent update.
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
