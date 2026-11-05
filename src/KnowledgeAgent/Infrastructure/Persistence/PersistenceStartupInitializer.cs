using System.Diagnostics;
using System.Net.Sockets;
using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeAgent.Infrastructure.Persistence;

internal sealed partial class PersistenceStartupInitializer(
    KnowledgeDbContext db,
    IOptions<PersistenceOptions> options,
    ILogger<PersistenceStartupInitializer> logger) : IStartupInitializer
{
    public static readonly TimeSpan DefaultRetryBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    public int Order => StartupOrder.Persistence;
    public string Name => "Persistence";
    public bool IsCritical => true;

    internal TimeSpan RetryBudget { get; init; } = DefaultRetryBudget;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            LogStoreCreated(logger, db.Database.ProviderName);
            return;
        }

        if (!options.Value.MigrateOnStartup)
        {
            LogMigrationsSkipped(logger);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var delay = InitialRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                await db.Database.MigrateAsync(cancellationToken);
                LogMigrated(logger, pending.Count, pending.Count == 0 ? "-" : string.Join(", ", pending));
                return;
            }
            catch (Exception ex) when (IsTransient(ex)
                && !cancellationToken.IsCancellationRequested
                && Stopwatch.GetElapsedTime(started) + delay <= RetryBudget)
            {
                LogMigrationRetry(logger, ex, attempt, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    internal static bool IsTransient(Exception exception) => exception switch
    {
        // PostgresException decides by SQL state, so 57P03 "the database system is starting up" counts as transient.
        NpgsqlException npgsql => npgsql.IsTransient,
        SocketException or TimeoutException or IOException => true,
        _ => exception.InnerException is { } inner && IsTransient(inner),
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Created the {Provider} store")]
    private static partial void LogStoreCreated(ILogger logger, string? provider);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Persistence:MigrateOnStartup is false; database migrations were not applied")]
    private static partial void LogMigrationsSkipped(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Database is up to date; applied {Count} migration(s): {Migrations}")]
    private static partial void LogMigrated(ILogger logger, int count, string migrations);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Database not reachable yet (attempt {Attempt}); retrying in {DelaySeconds}s")]
    private static partial void LogMigrationRetry(ILogger logger, Exception exception, int attempt, double delaySeconds);
}
