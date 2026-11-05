using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Web;

namespace KnowledgeAgent.Features.Alerts;

public sealed class AlertNotActiveException(string key) : Exception($"The alert '{key}' is not active.")
{
    public string Key { get; } = key;
}

internal sealed class AlertService(
    AlertEvaluator evaluator,
    IAlertRepository repository,
    IAlertSettingsStore settingsStore,
    IIntegrationSettings integrations,
    TimeProvider timeProvider)
{
    public const int MaxSnoozeHours = 24 * 30;
    public const int MaxWaitingMinutes = 24 * 60;

    public async Task<AlertListDto> ListAsync(AlertAudience audience, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var alerts = await evaluator.EvaluateAsync(audience, cancellationToken);
        var snoozes = (await repository.ListSnoozesAsync(cancellationToken)).ToDictionary(item => item.Key, StringComparer.Ordinal);
        var visible = alerts.Where(alert => !(snoozes.TryGetValue(alert.Key, out var snooze) && snooze.Hides(alert, now))).ToList();
        return new AlertListDto(visible.Select(ToDto).ToList(), alerts.Count - visible.Count);
    }

    public async Task SnoozeAsync(string key, int? hours, string? user, CancellationToken cancellationToken = default)
    {
        if (hours is < 1 or > MaxSnoozeHours)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["hours"] = [$"Snooze for 1 to {MaxSnoozeHours} hours."] });
        }

        var alert = (await evaluator.EvaluateAsync(AlertAudience.Admin, cancellationToken)).FirstOrDefault(item => item.Key == key)
            ?? throw new AlertNotActiveException(key);
        var now = timeProvider.GetUtcNow();
        await repository.SaveSnoozeAsync(
            new AlertSnooze
            {
                Key = alert.Key,
                Kind = alert.Kind,
                Count = alert.Count,
                Severity = alert.Severity,
                Until = hours is { } value ? now.AddHours(value) : null,
                SnoozedBy = user is { Length: > 200 } ? user[..200] : user,
                CreatedAt = now,
            },
            cancellationToken);
    }

    public async Task<AlertSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await ToDtoAsync(await settingsStore.GetAsync(cancellationToken), cancellationToken);

    public async Task<AlertSettingsDto> SaveSettingsAsync(SaveAlertSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        var kinds = request.Kinds ?? AlertKinds.All;
        foreach (var unknown in kinds.Where(kind => !AlertKinds.IsKnown(kind)))
        {
            errors.Add("kinds", $"'{unknown}' is not an alert. Choose from: {string.Join(", ", AlertKinds.All)}.");
        }

        var waiting = request.WaitingMinutes ?? AlertNotificationSettings.DefaultWaitingMinutes;
        if (waiting is < 1 or > MaxWaitingMinutes)
        {
            errors.Add("waitingMinutes", $"Enter 1 to {MaxWaitingMinutes} minutes.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var settings = new AlertNotificationSettings
        {
            Slack = request.Slack,
            Email = request.Email,
            Kinds = AlertKinds.All.Where(kinds.Contains).ToList(),
            WaitingMinutes = waiting,
        };
        await settingsStore.SaveAsync(settings, cancellationToken);
        return await ToDtoAsync(settings, cancellationToken);
    }

    private async Task<AlertSettingsDto> ToDtoAsync(AlertNotificationSettings settings, CancellationToken cancellationToken)
    {
        var slack = await integrations.GetSlackAsync(cancellationToken);
        var email = await integrations.GetEmailAsync(cancellationToken);
        return new AlertSettingsDto(
            settings.Slack,
            settings.Email,
            settings.Kinds,
            settings.WaitingMinutes,
            slack.CanPost && slack.ChannelId is not null,
            email.CanSend && email.SupportAddress is not null,
            slack.ChannelId,
            email.SupportAddress,
            AlertKinds.All);
    }

    internal static AlertDto ToDto(Alert alert) =>
        new(alert.Key, alert.Kind, alert.Severity, alert.Title, alert.Detail, alert.Count, alert.Since, alert.Link);
}
