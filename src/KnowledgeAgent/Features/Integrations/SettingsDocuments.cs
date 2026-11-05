using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Integrations;

// Callers hold the DbContextGate.
internal static class SettingsDocuments
{
    public static Task<string?> ReadAsync(KnowledgeDbContext db, string key, CancellationToken cancellationToken) =>
        db.IntegrationSettings.AsNoTracking()
            .Where(item => item.Key == key)
            .Select(item => (string?)item.SettingsJson)
            .FirstOrDefaultAsync(cancellationToken);

    public static async Task WriteAsync(KnowledgeDbContext db, string key, string? json, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await WriteOnceAsync(db, key, json, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two admins saved the first settings at once; the later one wins.
            db.ChangeTracker.Clear();
            await WriteOnceAsync(db, key, json, now, cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static async Task WriteOnceAsync(KnowledgeDbContext db, string key, string? json, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var record = await db.IntegrationSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
        if (json is null)
        {
            if (record is not null)
            {
                db.IntegrationSettings.Remove(record);
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (record is null)
        {
            record = new IntegrationSettingsRecord { Key = key };
            db.IntegrationSettings.Add(record);
        }

        record.SettingsJson = json;
        record.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
