using System.Collections.Concurrent;
using KnowledgeAgent.Features.Sources.Folders;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

internal sealed class InMemorySourceFolderRepository : ISourceFolderRepository
{
    private readonly ConcurrentDictionary<Guid, SourceFolder> _folders = new();

    public SourceFolder Add(string name, Guid? parentId = null)
    {
        var folder = new SourceFolder { Id = Guid.CreateVersion7(), Name = name, ParentId = parentId, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch };
        _folders[folder.Id] = folder;
        return folder;
    }

    public SourceFolder AddInternal()
    {
        var folder = new SourceFolder
        {
            Id = SourceFolder.InternalId,
            Name = SourceFolder.InternalName,
            IsInternal = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
        _folders[folder.Id] = folder;
        return folder;
    }

    public Task<IReadOnlyList<SourceFolder>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceFolder>>(_folders.Values.OrderBy(folder => folder.Name).ToList());

    public Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_folders.GetValueOrDefault(id));

    public Task<SourceFolder> AddAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        _folders[folder.Id] = folder;
        return Task.FromResult(folder);
    }

    public Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        _folders[folder.Id] = folder;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_folders.TryRemove(id, out _));
}
