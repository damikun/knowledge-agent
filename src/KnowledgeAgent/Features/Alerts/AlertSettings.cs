using System.Text.Json;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Persistence;

namespace KnowledgeAgent.Features.Alerts;

public sealed record AlertNotificationSettings
{
    public const int DefaultWaitingMinutes = 5;

    public bool Slack { get; init; }
    public bool Email { get; init; }
    public List<string> Kinds { get; init; } = [.. AlertKinds.All];

    public int WaitingMinutes { get; init; } = DefaultWaitingMinutes;
}

public interface IAlertSettingsStore
{
    Task<AlertNotificationSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AlertNotificationSettings settings, CancellationToken cancellationToken = default);
}

internal sealed class AlertSettingsStore(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : IAlertSettingsStore
{
    public const string Key = "alerts";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AlertNotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var json = await SettingsDocuments.ReadAsync(db, Key, cancellationToken);
        return json is null ? new AlertNotificationSettings() : Read(json);
    }

    public async Task SaveAsync(AlertNotificationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        using var _ = await gate.EnterAsync(cancellationToken);
        await SettingsDocuments.WriteAsync(db, Key, json, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static AlertNotificationSettings Read(string json)
    {
        var settings = JsonSerializer.Deserialize<AlertNotificationSettings>(json, JsonOptions) ?? new AlertNotificationSettings();
        return settings with { Kinds = settings.Kinds.Where(AlertKinds.IsKnown).Distinct(StringComparer.Ordinal).ToList() };
    }
}
