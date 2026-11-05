using System.Security.Cryptography;
using System.Text.Json;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Integrations;

public sealed class IntegrationOptions
{
    public const string SectionName = "Integrations";

    public EmailSettings Email { get; set; } = new();
    public SlackSettings Slack { get; set; } = new();
}

public sealed class IntegrationSettingsRecord
{
    public const string EmailKey = "email";
    public const string SlackKey = "slack";

    public string Key { get; set; } = string.Empty;

    public string SettingsJson { get; set; } = "{}";

    // Secret name to value, each encrypted with Data Protection.
    public string SecretsJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class IntegrationSettingsConfiguration(bool isNpgsql) : IEntityTypeConfiguration<IntegrationSettingsRecord>
{
    public void Configure(EntityTypeBuilder<IntegrationSettingsRecord> builder)
    {
        builder.ToTable("integration_settings");
        builder.HasKey(record => record.Key);
        builder.Property(record => record.Key).HasMaxLength(32);
        if (isNpgsql)
        {
            builder.Property(record => record.SettingsJson).HasColumnType("jsonb");
            builder.Property(record => record.SecretsJson).HasColumnType("text");
        }
    }
}

public interface IIntegrationSettings
{
    Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default);
    Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default);

    Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default);
    Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default);
    Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default);

    event Action? Changed;
}

// Short cache: other instances see a save once it expires.
internal sealed partial class IntegrationSettingsStore(
    IServiceScopeFactory scopes,
    IOptionsMonitor<IntegrationOptions> options,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<IntegrationSettingsStore> logger) : IIntegrationSettings, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Changing the purpose makes every saved secret undecryptable.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("KnowledgeAgent.Integrations.Secrets.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Swapped whole and read without the gate. A save bumps the generation, so a load that read the rows before the
    // save does not cache them after it.
    private Snapshot? _cache;
    private long _generation;

    public event Action? Changed;

    public async Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(IntegrationSettingsRecord.EmailKey, cancellationToken);
        if (record is null)
        {
            return options.CurrentValue.Email;
        }

        var secrets = Unprotect(record);
        return (JsonSerializer.Deserialize<EmailSettings>(record.SettingsJson, JsonOptions) ?? new EmailSettings()) with
        {
            SmtpPassword = secrets.GetValueOrDefault(nameof(EmailSettings.SmtpPassword)),
            ImapPassword = secrets.GetValueOrDefault(nameof(EmailSettings.ImapPassword)),
        };
    }

    public async Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(IntegrationSettingsRecord.SlackKey, cancellationToken);
        if (record is null)
        {
            return options.CurrentValue.Slack;
        }

        var secrets = Unprotect(record);
        return (JsonSerializer.Deserialize<SlackSettings>(record.SettingsJson, JsonOptions) ?? new SlackSettings()) with
        {
            BotToken = secrets.GetValueOrDefault(nameof(SlackSettings.BotToken)),
            AppToken = secrets.GetValueOrDefault(nameof(SlackSettings.AppToken)),
        };
    }

    public async Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default) => (
        await GetRecordAsync(IntegrationSettingsRecord.EmailKey, cancellationToken) is not null,
        await GetRecordAsync(IntegrationSettingsRecord.SlackKey, cancellationToken) is not null);

    public Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default) =>
        SaveAsync(
            IntegrationSettingsRecord.EmailKey,
            settings with { SmtpPassword = null, ImapPassword = null },
            new Dictionary<string, string?>
            {
                [nameof(EmailSettings.SmtpPassword)] = settings.SmtpPassword,
                [nameof(EmailSettings.ImapPassword)] = settings.ImapPassword,
            },
            cancellationToken);

    public Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default) =>
        SaveAsync(
            IntegrationSettingsRecord.SlackKey,
            settings with { BotToken = null, AppToken = null },
            new Dictionary<string, string?>
            {
                [nameof(SlackSettings.BotToken)] = settings.BotToken,
                [nameof(SlackSettings.AppToken)] = settings.AppToken,
            },
            cancellationToken);

    private async Task SaveAsync<T>(string key, T settings, Dictionary<string, string?> secrets, CancellationToken cancellationToken)
    {
        var protectedSecrets = secrets
            .Where(secret => !string.IsNullOrEmpty(secret.Value))
            .ToDictionary(secret => secret.Key, secret => _protector.Protect(secret.Value!));
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
            var record = await db.IntegrationSettings.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
            if (record is null)
            {
                record = new IntegrationSettingsRecord { Key = key };
                db.IntegrationSettings.Add(record);
            }

            record.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
            record.SecretsJson = JsonSerializer.Serialize(protectedSecrets, JsonOptions);
            record.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _cache, null);
        LogSaved(logger, key);
        Changed?.Invoke();
    }

    private async Task<IntegrationSettingsRecord?> GetRecordAsync(string key, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (Volatile.Read(ref _cache) is { } cached && now - cached.LoadedAt < CacheLifetime)
        {
            return cached.Records.GetValueOrDefault(key);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _cache) is { } fresh && now - fresh.LoadedAt < CacheLifetime)
            {
                return fresh.Records.GetValueOrDefault(key);
            }

            var generation = Interlocked.Read(ref _generation);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
            var records = await db.IntegrationSettings.AsNoTracking().ToDictionaryAsync(item => item.Key, cancellationToken);
            if (generation == Interlocked.Read(ref _generation))
            {
                Volatile.Write(ref _cache, new Snapshot(now, records));
            }

            return records.GetValueOrDefault(key);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private Dictionary<string, string> Unprotect(IntegrationSettingsRecord record)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(record.SecretsJson, JsonOptions) ?? [])
        {
            try
            {
                result[name] = _protector.Unprotect(value);
            }
            catch (CryptographicException ex)
            {
                // A lost key ring makes the secret unreadable; the integration then reports itself as not set up.
                LogUnprotectFailed(logger, ex, record.Key, name);
            }
        }

        return result;
    }

    private sealed record Snapshot(DateTimeOffset LoadedAt, Dictionary<string, IntegrationSettingsRecord> Records);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved the {Integration} integration settings")]
    private static partial void LogSaved(ILogger logger, string integration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Secret} of the {Integration} integration could not be decrypted; save it again")]
    private static partial void LogUnprotectFailed(ILogger logger, Exception exception, string integration, string secret);
}
