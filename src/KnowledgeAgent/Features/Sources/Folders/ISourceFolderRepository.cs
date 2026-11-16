namespace KnowledgeAgent.Features.Sources.Folders;

public interface ISourceFolderRepository
{
    Task<IReadOnlyList<SourceFolder>> ListAsync(CancellationToken cancellationToken = default);
    Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SourceFolder> AddAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
