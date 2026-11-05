using System.Globalization;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;

namespace KnowledgeAgent.Features.Alerts;

public enum AlertAudience
{
    Admin,

    Support,
}

public static class AlertRules
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    public const int CriticalWaitFactor = 3;

    // Slack reconnects on its own after a short drop, so only a longer one is an alert.
    public static readonly TimeSpan SlackGrace = TimeSpan.FromMinutes(2);

    public const int BaselineDays = 7;
    public const int MinRecentRatings = 5;
    public const int MinBaselineRatings = 10;
    public const double RatingDrop = 0.75;
    public const int MinRecentThumbs = 10;
    public const int MinBaselineThumbs = 20;
    public const double ThumbsUpShareDrop = 0.15;

    public const int MinUnansweredToday = 5;
    public const double UnansweredFactor = 2;

    // Quality needs several queries over a week of data; it changes slowly, so it is worked out at most this often.
    public static readonly TimeSpan QualityCacheLifetime = TimeSpan.FromMinutes(5);
}

internal sealed class SlackOutageTracker
{
    private DateTimeOffset? _downSince;

    public DateTimeOffset? Observe(bool down, DateTimeOffset now)
    {
        if (!down)
        {
            _downSince = null;
            return null;
        }

        _downSince ??= now;
        return _downSince;
    }
}

internal sealed class AlertQualityCache
{
    public (DateTimeOffset At, IReadOnlyList<Alert> Alerts)? Entry { get; set; }
}

internal sealed partial class AlertEvaluator(
    IAlertRepository repository,
    IAlertSettingsStore settingsStore,
    IModelProfileResolver models,
    IProviderSettingsStore providerSettings,
    IProviderErrorLog providerErrors,
    IIntegrationSettings integrations,
    ISlackConnectionState slackConnection,
    IAnalyticsRepository analytics,
    SlackOutageTracker slackOutage,
    AlertQualityCache qualityCache,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<Alert>> EvaluateAsync(AlertAudience audience, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await settingsStore.GetAsync(cancellationToken);

        var raised = (await repository.ListStatesAsync(cancellationToken))
            .Where(state => state.IsActive)
            .ToDictionary(state => state.Key, state => state.RaisedAt, StringComparer.Ordinal);
        DateTimeOffset Since(string key, DateTimeOffset? since = null) => since ?? raised.GetValueOrDefault(key, now);

        var alerts = new List<Alert>();
        await AddHandoverAlertsAsync(alerts, settings, now, Since, cancellationToken);
        if (audience == AlertAudience.Admin)
        {
            await AddModelAlertsAsync(alerts, now, Since, cancellationToken);
            await AddSourceAlertsAsync(alerts, Since, cancellationToken);
            await AddIntegrationAlertsAsync(alerts, now, Since, cancellationToken);
            alerts.AddRange(await QualityAlertsAsync(now, Since, cancellationToken));
        }

        return alerts
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => AlertKinds.All.ToList().IndexOf(alert.Kind))
            .ThenBy(alert => alert.Since)
            .ToList();
    }

    private async Task AddHandoverAlertsAsync(
        List<Alert> alerts,
        AlertNotificationSettings settings,
        DateTimeOffset now,
        Func<string, DateTimeOffset?, DateTimeOffset> since,
        CancellationToken cancellationToken)
    {
        var threshold = TimeSpan.FromMinutes(settings.WaitingMinutes);
        var waiting = await repository.CountWaitingForPersonAsync(now - threshold, cancellationToken);
        if (waiting is { Count: > 0, Since: { } oldest })
        {
            var waited = now - oldest;
            alerts.Add(new Alert(
                AlertKinds.VisitorsWaiting,
                AlertKinds.VisitorsWaiting,
                waited >= threshold * AlertRules.CriticalWaitFactor ? AlertSeverity.Critical : AlertSeverity.Warning,
                waiting.Count == 1 ? "A visitor is waiting for a person" : $"{waiting.Count} visitors are waiting for a person",
                $"{(waiting.Count == 1 ? "They have" : "The first has")} waited {Minutes(waited)}. Join the conversation in the Inbox.",
                waiting.Count,
                oldest,
                "/inbox"));
        }

        var missed = await repository.CountMissedRequestsAsync(now - AlertRules.RecentWindow, cancellationToken);
        if (missed.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKinds.MissedRequests,
                AlertKinds.MissedRequests,
                AlertSeverity.Warning,
                missed.Count == 1 ? "A request for a person was missed" : $"{missed.Count} requests for a person were missed",
                "Nobody joined in time in the last 24 hours and nobody has replied since. You can still reply to them.",
                missed.Count,
                since(AlertKinds.MissedRequests, missed.Since),
                "/inbox?view=all"));
        }
    }

    private async Task AddModelAlertsAsync(List<Alert> alerts, DateTimeOffset now, Func<string, DateTimeOffset?, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        var profiles = await models.ListAsync(cancellationToken);
        var chat = profiles.FirstOrDefault(profile => string.Equals(profile.Name, AgentDefaults.ProfileName, StringComparison.OrdinalIgnoreCase));
        var embedding = profiles.FirstOrDefault(profile => string.Equals(profile.Name, EmbeddingService.ProfileName, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(profile => profile.Kind == ModelKind.Embedding);
        if (chat is { Effective: null })
        {
            var key = $"{AlertKinds.ModelMissing}.chat";
            alerts.Add(new Alert(
                key,
                AlertKinds.ModelMissing,
                AlertSeverity.Critical,
                "No chat model is connected",
                "The agent cannot answer anyone. Add an API key for a model provider.",
                1,
                since(key, null),
                "/models#providers"));
        }

        if (embedding is { Effective: null })
        {
            var key = $"{AlertKinds.ModelMissing}.embedding";
            alerts.Add(new Alert(
                key,
                AlertKinds.ModelMissing,
                AlertSeverity.Critical,
                "No embedding model is connected",
                "New sources cannot be indexed and the agent cannot search them. Add a provider that offers embeddings.",
                1,
                since(key, null),
                "/models#providers"));
        }

        // A key saved after the rejection may well work, so the rejection waits for the next call to tell.
        var saved = (await providerSettings.ListAsync(cancellationToken)).ToDictionary(item => item.Key, item => item.UpdatedAt, StringComparer.OrdinalIgnoreCase);
        foreach (var rejection in providerErrors.ListRejections(now - AlertRules.RecentWindow))
        {
            if (saved.TryGetValue(rejection.ProviderKey, out var updatedAt) && updatedAt > rejection.At)
            {
                continue;
            }

            alerts.Add(new Alert(
                $"{AlertKinds.ProviderRejected}.{KeyPart(rejection.ProviderKey)}",
                AlertKinds.ProviderRejected,
                AlertSeverity.Critical,
                $"The {rejection.ProviderKey} API key was rejected",
                $"The provider answered {rejection.Status} {(rejection.Status == 401 ? "Unauthorized" : "Forbidden")} to a model call. Add a working key in Models.",
                1,
                rejection.At,
                "/models#providers"));
        }
    }

    private async Task AddSourceAlertsAsync(List<Alert> alerts, Func<string, DateTimeOffset?, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        var sources = await repository.GetSourceFactsAsync(cancellationToken);
        if (sources.Failed.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKinds.SourcesFailed,
                AlertKinds.SourcesFailed,
                AlertSeverity.Warning,
                sources.Failed.Count == 1 ? "A source could not be indexed" : $"{sources.Failed.Count} sources could not be indexed",
                "The agent cannot answer from them. See why in Sources and try again.",
                sources.Failed.Count,
                since(AlertKinds.SourcesFailed, sources.Failed.Since),
                "/sources"));
        }

        if (sources.Waiting.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKinds.SourcesWaiting,
                AlertKinds.SourcesWaiting,
                AlertSeverity.Warning,
                sources.Waiting.Count == 1 ? "A source is waiting for an embedding model" : $"{sources.Waiting.Count} sources are waiting for an embedding model",
                "They are indexed on their own once an embedding model is connected.",
                sources.Waiting.Count,
                since(AlertKinds.SourcesWaiting, sources.Waiting.Since),
                "/models#providers"));
        }
    }

    private async Task AddIntegrationAlertsAsync(List<Alert> alerts, DateTimeOffset now, Func<string, DateTimeOffset?, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        var failures = await repository.CountDeliveryFailuresAsync(now - AlertRules.RecentWindow, cancellationToken);
        var slack = await integrations.GetSlackAsync(cancellationToken);
        var downSince = slackOutage.Observe(slack.CanReceive && !slackConnection.IsConnected, now);
        var disconnected = downSince is { } down && now - down >= AlertRules.SlackGrace;
        if (disconnected || failures.Slack.Count > 0)
        {
            var details = new List<string>();
            if (disconnected)
            {
                details.Add("Replies and buttons in Slack do not reach the visitors right now.");
            }

            if (failures.Slack.Count > 0)
            {
                details.Add($"{Requests(failures.Slack.Count)} in the last 24 hours could not be posted{LastError(failures.LastSlackError)}");
            }

            details.Add("They are still in the Inbox.");
            var earliest = new[] { disconnected ? downSince : null, failures.Slack.Since }.OfType<DateTimeOffset>().Min();
            alerts.Add(new Alert(
                AlertKinds.SlackFailing,
                AlertKinds.SlackFailing,
                AlertSeverity.Warning,
                failures.Slack.Count > 0 ? "Requests could not be posted to Slack" : "Slack is not connected",
                string.Join(' ', details),
                Math.Max(1, failures.Slack.Count),
                since(AlertKinds.SlackFailing, earliest),
                "/notifications"));
        }

        if (failures.Email.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKinds.EmailFailing,
                AlertKinds.EmailFailing,
                AlertSeverity.Warning,
                "Requests could not be emailed",
                $"{Requests(failures.Email.Count)} in the last 24 hours could not be sent{LastError(failures.LastEmailError)} They are still in the Inbox.",
                failures.Email.Count,
                since(AlertKinds.EmailFailing, failures.Email.Since),
                "/notifications"));
        }
    }

    private async Task<IReadOnlyList<Alert>> QualityAlertsAsync(DateTimeOffset now, Func<string, DateTimeOffset?, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        if (qualityCache.Entry is { } cached && now - cached.At < AlertRules.QualityCacheLifetime)
        {
            return cached.Alerts;
        }

        var alerts = new List<Alert>();
        var recentFrom = now - AlertRules.RecentWindow;
        var recent = await repository.TallyFeedbackAsync(recentFrom, now, cancellationToken);
        var baseline = await repository.TallyFeedbackAsync(recentFrom.AddDays(-AlertRules.BaselineDays), recentFrom, cancellationToken);

        if (recent is { Ratings: >= AlertRules.MinRecentRatings, AverageRating: { } recentAverage }
            && baseline is { Ratings: >= AlertRules.MinBaselineRatings, AverageRating: { } baselineAverage }
            && baselineAverage - recentAverage >= AlertRules.RatingDrop)
        {
            alerts.Add(new Alert(
                AlertKinds.RatingDrop,
                AlertKinds.RatingDrop,
                AlertSeverity.Warning,
                "Conversation ratings dropped",
                $"The last 24 hours average {Number(recentAverage)} out of 5 from {recent.Ratings} ratings, against {Number(baselineAverage)} in the 7 days before. Read the low ratings in Feedback.",
                1,
                since(AlertKinds.RatingDrop, null),
                "/feedback"));
        }

        if (recent is { Thumbs: >= AlertRules.MinRecentThumbs, ThumbsUpShare: { } recentShare }
            && baseline is { Thumbs: >= AlertRules.MinBaselineThumbs, ThumbsUpShare: { } baselineShare }
            && baselineShare - recentShare >= AlertRules.ThumbsUpShareDrop)
        {
            alerts.Add(new Alert(
                AlertKinds.ThumbsDrop,
                AlertKinds.ThumbsDrop,
                AlertSeverity.Warning,
                "Fewer answers get a thumbs up",
                $"{Percent(recentShare)} of {recent.Thumbs} answers rated in the last 24 hours got a thumbs up, against {Percent(baselineShare)} in the 7 days before. Read the thumbs down in Feedback.",
                1,
                since(AlertKinds.ThumbsDrop, null),
                "/feedback"));
        }

        if (await UnansweredSpikeAsync(now, since, cancellationToken) is { } spike)
        {
            alerts.Add(spike);
        }

        qualityCache.Entry = (now, alerts);
        return alerts;
    }

    // Analytics counts by UTC day, so today so far is compared with the average day of the week before.
    private async Task<Alert?> UnansweredSpikeAsync(DateTimeOffset now, Func<string, DateTimeOffset?, DateTimeOffset> since, CancellationToken cancellationToken)
    {
        var today = await analytics.GetAsync(now, 1, cancellationToken);
        var week = await analytics.GetAsync(now, AlertRules.BaselineDays + 1, cancellationToken);
        static int Signals(AnalyticsTotalsDto totals) => totals.HandedToPerson + totals.ThumbsDown + totals.NoSourceAnswers;
        var todayCount = Signals(today.Totals);
        var average = Math.Max(0, Signals(week.Totals) - todayCount) / (double)AlertRules.BaselineDays;
        if (todayCount < AlertRules.MinUnansweredToday || todayCount < Math.Max(1, average) * AlertRules.UnansweredFactor)
        {
            return null;
        }

        var top = today.Unanswered.FirstOrDefault();
        var example = top is null ? string.Empty : $" Asked most: \"{Shorten(top.Question, 80)}\".";
        return new Alert(
            AlertKinds.UnansweredSpike,
            AlertKinds.UnansweredSpike,
            AlertSeverity.Warning,
            "More questions went unanswered today",
            $"{todayCount} today against {Number(average)} on an average day of the week before.{example} Add what is missing to your sources.",
            1,
            since(AlertKinds.UnansweredSpike, null),
            "/analytics");
    }

    private static string Minutes(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Floor(span.TotalMinutes));
        if (minutes < 120)
        {
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        var hours = minutes / 60;
        return $"{hours} hours";
    }

    private static string Requests(int count) => count == 1 ? "1 request" : $"{count} requests";

    private static string LastError(string? error) => string.IsNullOrWhiteSpace(error) ? "." : $". Last error: {Shorten(error, 160).TrimEnd('.')}.";

    private static string Shorten(string text, int length)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= length ? flat : flat[..(length - 3)].TrimEnd() + "...";
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double share) => (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    // Provider keys come from configuration; the alert key keeps to letters, digits, dashes and underscores for URLs.
    internal static string KeyPart(string value) => UnsafeKeyCharacters().Replace(value.ToLowerInvariant(), "-");

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex UnsafeKeyCharacters();
}
