using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Sources.Folders;

internal sealed partial class SourceFolderService(
    ISourceFolderRepository folders,
    ISourceRepository sources,
    SourceFolderGate gate,
    TimeProvider timeProvider,
    ILogger<SourceFolderService> logger) : ISourceFolderService
{
    // A full page of the Sources list.
    public const int MaxSourcesPerMove = SourceQuery.MaxLimit;

    public async Task<SourceFolderListing> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await folders.ListAsync(cancellationToken).ConfigureAwait(false);
        var counts = await sources.CountByFolderAsync(cancellationToken).ConfigureAwait(false);
        return new SourceFolderListing(all, counts);
    }

    public Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        folders.GetAsync(id, cancellationToken);

    public async Task<int> CountSourcesAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await sources.CountByFolderAsync(cancellationToken).ConfigureAwait(false)).ByFolder.GetValueOrDefault(id);

    public async Task<SourceFolder> CreateAsync(string? name, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        var cleanName = ValidateName(name);
        using var _ = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var tree = await LoadTreeAsync(cancellationToken).ConfigureAwait(false);

        if (parentId is { } id)
        {
            if (tree.Find(id) is null)
            {
                throw new ValidationFailedException("parentId", "The parent folder does not exist.");
            }

            if (tree.DepthOf(id) >= SourceFolder.MaxDepth)
            {
                throw new ValidationFailedException("parentId", TooDeep);
            }
        }

        EnsureUniqueName(tree, "name", cleanName, parentId, except: null);
        var now = timeProvider.GetUtcNow();
        var folder = await folders.AddAsync(
            new SourceFolder { Id = Guid.CreateVersion7(now), Name = cleanName, ParentId = parentId, CreatedAt = now, UpdatedAt = now },
            cancellationToken).ConfigureAwait(false);
        LogCreated(logger, folder.Id);
        return folder;
    }

    public async Task<SourceFolder?> RenameAsync(Guid id, string? name, CancellationToken cancellationToken = default)
    {
        var cleanName = ValidateName(name);
        using var _ = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var tree = await LoadTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree.Find(id) is not { } folder)
        {
            return null;
        }

        EnsureNotInternal(folder, "renamed");
        if (!string.Equals(folder.Name, cleanName, StringComparison.Ordinal))
        {
            EnsureUniqueName(tree, "name", cleanName, folder.ParentId, except: id);
            folder.Name = cleanName;
            await folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
        }

        return folder;
    }

    public async Task<SourceFolder?> MoveAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        var tree = await LoadTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree.Find(id) is not { } folder)
        {
            return null;
        }

        EnsureNotInternal(folder, "moved");
        if (folder.ParentId == parentId)
        {
            return folder;
        }

        if (parentId is { } target)
        {
            if (tree.Find(target) is null)
            {
                throw new ValidationFailedException("parentId", "The parent folder does not exist.");
            }

            if (target == id || tree.IsInside(target, id))
            {
                throw new ValidationFailedException("parentId", "A folder cannot be moved into itself or one of its subfolders.");
            }

            if (tree.DepthOf(target) + tree.HeightOf(id) > SourceFolder.MaxDepth)
            {
                throw new ValidationFailedException("parentId", TooDeep);
            }
        }

        EnsureUniqueName(tree, "parentId", folder.Name, parentId, except: id);
        folder.ParentId = parentId;
        await folders.UpdateAsync(folder, cancellationToken).ConfigureAwait(false);
        return folder;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (await folders.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } folder)
        {
            EnsureNotInternal(folder, "deleted");
        }

        var deleted = await folders.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            LogDeleted(logger, id);
        }

        return deleted;
    }

    public async Task<int> MoveSourcesAsync(IReadOnlyCollection<Guid> sourceIds, Guid? folderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        var distinct = sourceIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            throw new ValidationFailedException("sourceIds", "Choose at least one source.");
        }

        if (distinct.Count > MaxSourcesPerMove)
        {
            throw new ValidationFailedException("sourceIds", $"At most {MaxSourcesPerMove} sources can be moved at once.");
        }

        return await MoveAsync(folderId, () => sources.MoveToFolderAsync(distinct, folderId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> MoveMatchingSourcesAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return MoveAsync(folderId, () => sources.MoveMatchingToFolderAsync(query, folderId, cancellationToken), cancellationToken);
    }

    private async Task<int> MoveAsync(Guid? folderId, Func<Task<int>> move, CancellationToken cancellationToken)
    {
        // Holding the gate keeps the folder from being deleted between the check and the move.
        using var _ = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (folderId is { } id && await folders.GetAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new ValidationFailedException("folderId", "The folder does not exist.");
        }

        var moved = await move().ConfigureAwait(false);
        LogSourcesMoved(logger, moved, folderId);
        return moved;
    }

    internal static string? CleanName(string? name)
    {
        var words = TextNormalizer.Normalize(name).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static string TooDeep => $"Folders can be nested at most {SourceFolder.MaxDepth} levels deep.";

    private static string ValidateName(string? name)
    {
        var cleanName = CleanName(name) ?? throw new ValidationFailedException("name", "A folder name is required.");
        if (cleanName.Length > SourceFolder.MaxNameLength)
        {
            throw new ValidationFailedException("name", $"The name must be at most {SourceFolder.MaxNameLength} characters long.");
        }

        return cleanName;
    }

    // Its subfolders are ordinary folders that happen to be inside it.
    private static void EnsureNotInternal(SourceFolder folder, string change)
    {
        if (folder.IsInternal)
        {
            throw new ValidationFailedException("id", $"The {SourceFolder.InternalName} folder is built in and cannot be {change}.");
        }
    }

    private static void EnsureUniqueName(SourceFolderTree tree, string field, string name, Guid? parentId, Guid? except)
    {
        var taken = tree.ChildrenOf(parentId)
            .Any(sibling => sibling.Id != except && string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            var location = parentId is { } id ? $"in '{tree.Find(id)?.Name}'" : "at the top level";
            throw new ValidationFailedException(field, $"A folder named '{name}' already exists {location}.");
        }
    }

    private async Task<SourceFolderTree> LoadTreeAsync(CancellationToken cancellationToken) =>
        new(await folders.ListAsync(cancellationToken).ConfigureAwait(false));

    [LoggerMessage(Level = LogLevel.Information, Message = "Created source folder {FolderId}")]
    private static partial void LogCreated(ILogger logger, Guid folderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted source folder {FolderId} and its subfolders")]
    private static partial void LogDeleted(ILogger logger, Guid folderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved {Count} sources to folder {FolderId}")]
    private static partial void LogSourcesMoved(ILogger logger, int count, Guid? folderId);
}
