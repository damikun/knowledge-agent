using System.Text.Json;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Persistence;

namespace KnowledgeAgent.Features.Help;

public sealed record HelpSettings
{
    public bool Enabled { get; init; } = true;
}

public interface IHelpSettingsStore
{
    Task<HelpSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(HelpSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class HelpSettingsStore(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : IHelpSettingsStore
{
    public const string Key = "help";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HelpSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var json = await SettingsDocuments.ReadAsync(db, Key, cancellationToken);
        return json is null ? new HelpSettings() : JsonSerializer.Deserialize<HelpSettings>(json, JsonOptions) ?? new HelpSettings();
    }

    public async Task SaveAsync(HelpSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        using var _ = await gate.EnterAsync(cancellationToken);
        await SettingsDocuments.WriteAsync(db, Key, json, timeProvider.GetUtcNow(), cancellationToken);
    }
}
