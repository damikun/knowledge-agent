using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Evaluations.Datasets;

public sealed record CustomDatasetSummary(CustomDatasetRecord Dataset, int ItemCount, IReadOnlyList<string> Tags);

public interface ICustomDatasetRepository
{
    Task<IReadOnlyList<CustomDatasetSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<CustomDatasetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomDatasetRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> AddAsync(CustomDatasetRecord dataset, IReadOnlyList<CustomDatasetItemRecord> items, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(CustomDatasetRecord dataset, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomDatasetItemRecord>> ListItemsAsync(Guid datasetId, CancellationToken cancellationToken = default);

    Task<int> CountItemsAsync(Guid datasetId, CancellationToken cancellationToken = default);

    Task<CustomDatasetItemRecord?> GetItemAsync(Guid datasetId, Guid itemId, CancellationToken cancellationToken = default);

    Task AddItemAsync(CustomDatasetItemRecord item, CancellationToken cancellationToken = default);

    Task AddItemsAsync(Guid datasetId, IReadOnlyList<CustomDatasetItemRecord> items, CancellationToken cancellationToken = default);

    Task<bool> UpdateItemAsync(CustomDatasetItemRecord item, CancellationToken cancellationToken = default);

    Task<int> DeleteItemsAsync(Guid datasetId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(Guid datasetId, Guid itemId, CancellationToken cancellationToken = default);
}

internal sealed class CustomDatasetRepository(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : ICustomDatasetRepository
{
    public async Task<IReadOnlyList<CustomDatasetSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var datasets = await db.EvalDatasets.AsNoTracking().OrderBy(dataset => dataset.NormalizedName).ToListAsync(cancellationToken);
        var items = await db.EvalDatasetItems.AsNoTracking()
            .Select(item => new { item.DatasetId, item.Tags })
            .ToListAsync(cancellationToken);
        var byDataset = items.ToLookup(item => item.DatasetId);
        return datasets.Select(dataset => new CustomDatasetSummary(
                dataset,
                byDataset[dataset.Id].Count(),
                byDataset[dataset.Id].SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    public async Task<CustomDatasetRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalDatasets.AsNoTracking().FirstOrDefaultAsync(dataset => dataset.Id == id, cancellationToken);
    }

    public async Task<CustomDatasetRecord?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = CustomDatasetRecord.Normalize(name);
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalDatasets.AsNoTracking().FirstOrDefaultAsync(dataset => dataset.NormalizedName == normalized, cancellationToken);
    }

    public async Task<bool> AddAsync(CustomDatasetRecord dataset, IReadOnlyList<CustomDatasetItemRecord> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(items);
        dataset.NormalizedName = CustomDatasetRecord.Normalize(dataset.Name);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            // The in-memory store has no unique indexes.
            if (!db.Database.IsRelational() && await db.EvalDatasets.AnyAsync(other => other.NormalizedName == dataset.NormalizedName, cancellationToken))
            {
                return false;
            }

            db.EvalDatasets.Add(dataset);
            foreach (var item in items)
            {
                item.DatasetId = dataset.Id;
                db.EvalDatasetItems.Add(item);
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> UpdateAsync(CustomDatasetRecord dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        dataset.NormalizedName = CustomDatasetRecord.Normalize(dataset.Name);
        dataset.UpdatedAt = timeProvider.GetUtcNow();
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!db.Database.IsRelational()
                && await db.EvalDatasets.AnyAsync(other => other.NormalizedName == dataset.NormalizedName && other.Id != dataset.Id, cancellationToken))
            {
                return false;
            }

            var entry = db.EvalDatasets.Attach(dataset);
            entry.Property(item => item.Name).IsModified = true;
            entry.Property(item => item.NormalizedName).IsModified = true;
            entry.Property(item => item.Description).IsModified = true;
            entry.Property(item => item.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return false;
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
            return await db.EvalDatasets.Where(dataset => dataset.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        // Non-relational providers lack ExecuteDelete and store-side cascades.
        try
        {
            var dataset = await db.EvalDatasets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (dataset is null)
            {
                return false;
            }

            db.EvalDatasetItems.RemoveRange(await db.EvalDatasetItems.Where(item => item.DatasetId == id).ToListAsync(cancellationToken));
            db.EvalDatasets.Remove(dataset);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<CustomDatasetItemRecord>> ListItemsAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalDatasetItems.AsNoTracking()
            .Where(item => item.DatasetId == datasetId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountItemsAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalDatasetItems.CountAsync(item => item.DatasetId == datasetId, cancellationToken);
    }

    public async Task<CustomDatasetItemRecord?> GetItemAsync(Guid datasetId, Guid itemId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalDatasetItems.AsNoTracking().FirstOrDefaultAsync(item => item.Id == itemId && item.DatasetId == datasetId, cancellationToken);
    }

    public async Task AddItemAsync(CustomDatasetItemRecord item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.EvalDatasetItems.Add(item);
            Touch(item.DatasetId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new KeyNotFoundException($"Dataset '{item.DatasetId}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task AddItemsAsync(Guid datasetId, IReadOnlyList<CustomDatasetItemRecord> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            // One SaveChanges is one transaction, so a failed batch leaves nothing behind.
            foreach (var item in items)
            {
                item.DatasetId = datasetId;
                db.EvalDatasetItems.Add(item);
            }

            Touch(datasetId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            throw new KeyNotFoundException($"Dataset '{datasetId}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<int> DeleteItemsAsync(Guid datasetId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var items = await db.EvalDatasetItems.Where(item => item.DatasetId == datasetId && itemIds.Contains(item.Id)).ToListAsync(cancellationToken);
            if (items.Count == 0)
            {
                return 0;
            }

            db.EvalDatasetItems.RemoveRange(items);
            Touch(datasetId);
            await db.SaveChangesAsync(cancellationToken);
            return items.Count;
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> UpdateItemAsync(CustomDatasetItemRecord item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!await db.EvalDatasetItems.AnyAsync(existing => existing.Id == item.Id && existing.DatasetId == item.DatasetId, cancellationToken))
            {
                return false;
            }

            db.EvalDatasetItems.Update(item);
            Touch(item.DatasetId);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteItemAsync(Guid datasetId, Guid itemId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var item = await db.EvalDatasetItems.FirstOrDefaultAsync(existing => existing.Id == itemId && existing.DatasetId == datasetId, cancellationToken);
            if (item is null)
            {
                return false;
            }

            db.EvalDatasetItems.Remove(item);
            Touch(datasetId);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private void Touch(Guid datasetId)
    {
        var entry = db.EvalDatasets.Attach(new CustomDatasetRecord { Id = datasetId, UpdatedAt = timeProvider.GetUtcNow() });
        entry.Property(dataset => dataset.UpdatedAt).IsModified = true;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
