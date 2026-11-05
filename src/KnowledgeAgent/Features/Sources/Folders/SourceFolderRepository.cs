using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Sources.Folders;

internal sealed class SourceFolderRepository(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : ISourceFolderRepository
{
    public async Task<IReadOnlyList<SourceFolder>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.SourceFolders.AsNoTracking()
            .OrderBy(folder => folder.Name)
            .ThenBy(folder => folder.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<SourceFolder?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.SourceFolders.AsNoTracking().FirstOrDefaultAsync(folder => folder.Id == id, cancellationToken);
    }

    public async Task<SourceFolder> AddAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            db.SourceFolders.Add(folder);
            await db.SaveChangesAsync(cancellationToken);
            return folder;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateAsync(SourceFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var _ = await gate.EnterAsync(cancellationToken);
        folder.UpdatedAt = timeProvider.GetUtcNow();

        try
        {
            db.Entry(folder).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Folder '{folder.Id}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);

        if (db.Database.IsRelational())
        {
            // The foreign keys delete the subfolders and set folder_id to NULL on their sources.
            return await db.SourceFolders.Where(folder => folder.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        // Non-relational providers have no store-side foreign keys, so the cascade happens here.
        try
        {
            var folders = await db.SourceFolders.ToListAsync(cancellationToken);
            var deleted = new SourceFolderTree(folders).SubtreeOf(id);
            if (deleted.Count == 0)
            {
                return false;
            }

            var deletedIds = deleted.Select(folder => (Guid?)folder.Id).ToList();
            var sources = await db.Sources.Where(source => deletedIds.Contains(source.FolderId)).ToListAsync(cancellationToken);
            foreach (var source in sources)
            {
                source.FolderId = null;
            }

            db.SourceFolders.RemoveRange(deleted);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
