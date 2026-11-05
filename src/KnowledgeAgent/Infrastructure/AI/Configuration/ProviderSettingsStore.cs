using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal sealed class ProviderSettingsStore(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider)
    : IProviderSettingsStore
{
    public async Task<IReadOnlyList<ProviderSettingsOverride>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var settings = await db.ProviderSettingsOverrides.AsNoTracking().ToListAsync(cancellationToken);
        return settings
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ProviderSettingsOverride?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var _ = await gate.EnterAsync(cancellationToken);
        return Match(await db.ProviderSettingsOverrides.AsNoTracking().ToListAsync(cancellationToken), key);
    }

    public async Task UpsertAsync(ProviderSettingsOverride settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Key);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var stored = Match(await db.ProviderSettingsOverrides.ToListAsync(cancellationToken), settings.Key);
            if (stored is null)
            {
                stored = new ProviderSettingsOverride { Key = settings.Key };
                db.ProviderSettingsOverrides.Add(stored);
            }

            stored.ProtectedApiKey = settings.ProtectedApiKey;
            stored.ApiKeyHint = settings.ApiKeyHint;
            stored.Endpoint = settings.Endpoint;
            stored.DefaultChatModel = settings.DefaultChatModel;
            stored.DefaultEmbeddingModel = settings.DefaultEmbeddingModel;
            stored.DefaultEmbeddingDimensions = settings.DefaultEmbeddingDimensions;
            stored.UpdatedAt = timeProvider.GetUtcNow();

            await db.SaveChangesAsync(cancellationToken);
            settings.UpdatedAt = stored.UpdatedAt;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var stored = Match(await db.ProviderSettingsOverrides.ToListAsync(cancellationToken), key);
            if (stored is null)
            {
                return false;
            }

            db.ProviderSettingsOverrides.Remove(stored);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // Matched in memory: the table holds a handful of rows and this behaves the same on every database provider.
    private static ProviderSettingsOverride? Match(List<ProviderSettingsOverride> settings, string key) =>
        settings.Find(entry => string.Equals(entry.Key, key, StringComparison.Ordinal))
        ?? settings
            .Where(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .FirstOrDefault();
}
