using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal sealed class ModelProfileOverrideStore(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider)
    : IModelProfileOverrideStore
{
    public async Task<IReadOnlyList<ModelProfileOverride>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var overrides = await db.ModelProfileOverrides.AsNoTracking().ToListAsync(cancellationToken);
        return overrides
            .OrderBy(profileOverride => profileOverride.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profileOverride => profileOverride.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ModelProfileOverride?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var _ = await gate.EnterAsync(cancellationToken);
        return Match(await db.ModelProfileOverrides.AsNoTracking().ToListAsync(cancellationToken), name);
    }

    public async Task UpsertAsync(ModelProfileOverride profileOverride, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileOverride);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileOverride.Name);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var stored = Match(await db.ModelProfileOverrides.ToListAsync(cancellationToken), profileOverride.Name);
            if (stored is null)
            {
                stored = new ModelProfileOverride { Name = profileOverride.Name };
                db.ModelProfileOverrides.Add(stored);
            }

            stored.Provider = profileOverride.Provider;
            stored.Model = profileOverride.Model;
            stored.Temperature = profileOverride.Temperature;
            stored.MaxOutputTokens = profileOverride.MaxOutputTokens;
            stored.ReasoningEffort = profileOverride.ReasoningEffort;
            stored.ReasoningOutput = profileOverride.ReasoningOutput;
            stored.Dimensions = profileOverride.Dimensions;
            stored.UpdatedAt = timeProvider.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);
            profileOverride.UpdatedAt = stored.UpdatedAt;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var stored = Match(await db.ModelProfileOverrides.ToListAsync(cancellationToken), name);
            if (stored is null)
            {
                return false;
            }

            db.ModelProfileOverrides.Remove(stored);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // Matched in memory: the table holds a handful of rows and this behaves the same on every database provider.
    private static ModelProfileOverride? Match(List<ModelProfileOverride> overrides, string name) =>
        overrides.Find(profileOverride => string.Equals(profileOverride.Name, name, StringComparison.Ordinal))
        ?? overrides
            .Where(profileOverride => string.Equals(profileOverride.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(profileOverride => profileOverride.Name, StringComparer.Ordinal)
            .FirstOrDefault();
}
