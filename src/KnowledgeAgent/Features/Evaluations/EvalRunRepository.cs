using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalRunRepository(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : IEvalRunRepository
{
    public async Task AddAsync(EvalRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.EvalRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<EvalRunRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<EvalRunRecord>> ListAsync(int take = 20, string? dataset = null, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var runs = db.EvalRuns.AsNoTracking();
        if (dataset is not null)
        {
            runs = runs.Where(run => run.Dataset == dataset);
        }

        return await runs
            .OrderByDescending(run => run.CreatedAt)
            .ThenByDescending(run => run.Id)
            .Take(Math.Max(1, take))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvalRunRecord>> ListByStatusAsync(IReadOnlyCollection<string> statuses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
        {
            return [];
        }

        var wanted = statuses.Distinct().ToArray();
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.EvalRuns.AsNoTracking()
            .Where(run => wanted.Contains(run.Status))
            .OrderBy(run => run.CreatedAt)
            .ThenBy(run => run.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(EvalRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        using var _ = await gate.EnterAsync(cancellationToken);
        run.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            db.Entry(run).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Eval run '{run.Id}' does not exist.", ex);
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
            return await db.EvalRuns.Where(run => run.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        try
        {
            var run = await db.EvalRuns.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (run is null)
            {
                return false;
            }

            db.EvalRuns.Remove(run);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
