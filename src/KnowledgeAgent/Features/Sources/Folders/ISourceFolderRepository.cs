namespace KnowledgeAgent.Features.Sources.Folders;

public interface ISourceFolderRepository
{
    Task<IReadOnlyList<SourceFolder>> ListAsync(CancellationToken cancellationToken = default);
    Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SourceFolder> AddAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    // Saves the name and parent and sets UpdatedAt.
    Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    // Also deletes the subfolders. The sources of every deleted folder become unfiled.
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
