namespace KnowledgeAgent.Features.Sources.Folders;

public interface ISourceFolderService
{
    Task<SourceFolderListing> ListAsync(CancellationToken cancellationToken = default);
    Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountSourcesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SourceFolder> CreateAsync(string? name, Guid? parentId = null, CancellationToken cancellationToken = default);
    Task<SourceFolder?> RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default);
    Task<SourceFolder?> MoveAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> MoveSourcesAsync(IReadOnlyCollection<Guid> sourceIds, Guid? folderId, CancellationToken cancellationToken = default);
    Task<int> MoveMatchingSourcesAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default);
}

public sealed record SourceFolderListing(IReadOnlyList<SourceFolder> Folders, SourceFolderCounts Counts);
