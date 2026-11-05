using KnowledgeAgent.Features.Sources;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

// Mimics EF Core's DbContext, which rejects a second operation while one is still running.
public sealed class SingleOperationSourceRepository(ISourceRepository inner) : ISourceRepository
{
    private int _active;

    public Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.AddAsync(source, content, cancellationToken));

    public Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.GetAsync(id, cancellationToken));

    public Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.ListAsync(cancellationToken));

    public Task<IReadOnlyList<Source>> ListByStatusAsync(IReadOnlyCollection<SourceStatus> statuses, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.ListByStatusAsync(statuses, cancellationToken));

    public Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.FindAsync(query, cancellationToken));

    public Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.GetStatsAsync(cancellationToken));

    public Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.CountByFolderAsync(cancellationToken));

    public Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.MoveToFolderAsync(ids, folderId, cancellationToken));

    public Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.MoveMatchingToFolderAsync(query, folderId, cancellationToken));

    public Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.CountBySiteAsync(query, limit, cancellationToken));

    public Task UpdateAsync(Source source, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            await inner.UpdateAsync(source, cancellationToken);
            return true;
        });

    public Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.GetContentAsync(sourceId, cancellationToken));

    public Task SaveContentAsync(Guid sourceId, string? extractedText, byte[]? rawBytes = null, CancellationToken cancellationToken = default) =>
        GuardAsync(async () =>
        {
            await inner.SaveContentAsync(sourceId, extractedText, rawBytes, cancellationToken);
            return true;
        });

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        GuardAsync(() => inner.DeleteAsync(id, cancellationToken));

    private async Task<T> GuardAsync<T>(Func<Task<T>> operation)
    {
        if (Interlocked.Increment(ref _active) > 1)
        {
            Interlocked.Decrement(ref _active);
            throw new InvalidOperationException("A second operation was started on this context instance before a previous operation completed.");
        }

        try
        {
            await Task.Delay(25);
            return await operation();
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
