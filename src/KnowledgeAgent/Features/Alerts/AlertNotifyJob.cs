using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace KnowledgeAgent.Features.Alerts;

// At most one message per kind per hour.
internal sealed partial class AlertNotifyJob(
    AlertEvaluator evaluator,
    IAlertRepository repository,
    IAlertSettingsStore settingsStore,
    IIntegrationSettings integrations,
    IPublicAddress publicAddress,
    ISlackGateway slack,
    IEmailTransport email,
    TimeProvider timeProvider,
    ILogger<AlertNotifyJob> logger) : IJobHandler
{
    public const string Kind = "alerts.notify";
    public const string ScheduleName = "alerts-notify";
    public static readonly TimeSpan PerKindInterval = TimeSpan.FromHours(1);

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var alerts = await evaluator.EvaluateAsync(AlertAudience.Admin, cancellationToken);
        var active = alerts.ToDictionary(alert => alert.Key, StringComparer.Ordinal);
        var states = (await repository.ListStatesAsync(cancellationToken)).ToDictionary(state => state.Key, StringComparer.Ordinal);
        var changed = new Dictionary<string, AlertState>(StringComparer.Ordinal);

        foreach (var alert in alerts)
        {
            if (!states.TryGetValue(alert.Key, out var state))
            {
                state = new AlertState { Key = alert.Key, Kind = alert.Kind, RaisedAt = now };
                states[alert.Key] = state;
            }
            else if (!state.IsActive)
            {
                state.RaisedAt = now;
                state.ResolvedAt = null;
            }

            state.LastSeenAt = now;
            changed[state.Key] = state;
        }

        foreach (var state in states.Values.Where(state => state.IsActive && !active.ContainsKey(state.Key)))
        {
            state.ResolvedAt = now;
            changed[state.Key] = state;
        }

        var snoozes = await repository.ListSnoozesAsync(cancellationToken);
        await repository.DeleteSnoozesAsync(
            snoozes.Where(snooze => !active.ContainsKey(snooze.Key) || snooze.Until <= now).Select(snooze => snooze.Key).ToList(),
            cancellationToken);
        var hiding = snoozes.ToDictionary(snooze => snooze.Key, StringComparer.Ordinal);

        var settings = await settingsStore.GetAsync(cancellationToken);
        var targets = await TargetsAsync(settings, cancellationToken);
        if (targets.Any)
        {
            var lastByKind = states.Values
                .Where(state => state.NotifiedAt is not null)
                .GroupBy(state => state.Kind, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(state => state.NotifiedAt!.Value), StringComparer.Ordinal);
            foreach (var alert in alerts)
            {
                var state = states[alert.Key];
                if (state.NotifiedForCurrent
                    || !settings.Kinds.Contains(alert.Kind, StringComparer.Ordinal)
                    || (hiding.TryGetValue(alert.Key, out var snooze) && snooze.Hides(alert, now))
                    || (lastByKind.TryGetValue(alert.Kind, out var last) && now - last < PerKindInterval))
                {
                    continue;
                }

                // Left unsent when every channel failed, so the next run tries again.
                if (await SendAsync(alert, targets, cancellationToken))
                {
                    state.NotifiedAt = now;
                    lastByKind[alert.Kind] = now;
                    changed[state.Key] = state;
                }
            }
        }

        await repository.SaveStatesAsync(changed.Values.ToList(), cancellationToken);
    }

    private async Task<Targets> TargetsAsync(AlertNotificationSettings settings, CancellationToken cancellationToken)
    {
        var channel = settings.Slack && await integrations.GetSlackAsync(cancellationToken) is { CanPost: true, ChannelId: { } id } ? id : null;
        var mail = settings.Email ? await integrations.GetEmailAsync(cancellationToken) : null;
        var portalUrl = (await publicAddress.GetAsync(cancellationToken)).Url;
        return new Targets(channel, mail is { CanSend: true, SupportAddress: not null } ? mail : null, portalUrl);
    }

    private async Task<bool> SendAsync(Alert alert, Targets targets, CancellationToken cancellationToken)
    {
        var sent = false;
        if (targets.SlackChannel is { } channel)
        {
            try
            {
                await slack.PostMessageAsync(channel, SlackText(alert, targets.PortalUrl), cancellationToken: cancellationToken);
                sent = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSendFailed(logger, ex, alert.Key, "slack");
            }
        }

        if (targets.Email is { } mail)
        {
            try
            {
                await email.SendAsync(Email(alert, mail, targets.PortalUrl), cancellationToken);
                sent = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogSendFailed(logger, ex, alert.Key, "email");
            }
        }

        if (sent)
        {
            LogSent(logger, alert.Key);
        }

        return sent;
    }

    internal static string SlackText(Alert alert, string? portalUrl)
    {
        var link = Link(alert, portalUrl);
        var fix = link is null ? string.Empty : $"\n<{link}|Fix it>";
        return $"{Icon(alert.Severity)} *{Escape(alert.Title)}*\n{Escape(alert.Detail)}{fix}";
    }

    internal static MimeMessage Email(Alert alert, EmailSettings settings, string? portalUrl)
    {
        var message = new MimeMessage { Subject = $"Knowledge Agent: {alert.Title}" };
        message.From.Add(new MailboxAddress(settings.FromName ?? "Knowledge Agent", settings.FromAddress!));
        message.To.Add(MailboxAddress.Parse(settings.SupportAddress!));
        var link = Link(alert, portalUrl);
        var body = $"{alert.Title}\n\n{alert.Detail}\n";
        body += link is null ? $"\nOpen {alert.Link} in the portal to fix it.\n" : $"\nFix it: {link}\n";
        body += "\nYou get one email per alert until it is resolved. Change this on the Notifications page.\n";
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }

    // Without the portal address the link cannot be absolute, and Slack would not open a bare path.
    private static string? Link(Alert alert, string? portalUrl) =>
        string.IsNullOrWhiteSpace(portalUrl) ? null : portalUrl.TrimEnd('/') + alert.Link;

    private static string Icon(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => ":red_circle:",
        AlertSeverity.Warning => ":large_orange_circle:",
        _ => ":large_blue_circle:",
    };

    private static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record Targets(string? SlackChannel, EmailSettings? Email, string? PortalUrl)
    {
        public bool Any => SlackChannel is not null || Email is not null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent the alert {AlertKey}")]
    private static partial void LogSent(ILogger logger, string alertKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The alert {AlertKey} could not be sent by {Channel}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string alertKey, string channel);
}
