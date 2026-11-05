using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Alerts;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public static class AlertKinds
{
    public const string SourcesFailed = "sources_failed";
    public const string SourcesWaiting = "sources_waiting";
    public const string ModelMissing = "model_missing";
    public const string ProviderRejected = "provider_rejected";
    public const string SlackFailing = "slack_failing";
    public const string EmailFailing = "email_failing";
    public const string VisitorsWaiting = "visitors_waiting";
    public const string MissedRequests = "missed_requests";
    public const string RatingDrop = "rating_drop";
    public const string ThumbsDrop = "thumbs_drop";
    public const string UnansweredSpike = "unanswered_spike";

    public static readonly IReadOnlyList<string> All =
    [
        VisitorsWaiting, MissedRequests, ModelMissing, ProviderRejected, SourcesFailed, SourcesWaiting,
        SlackFailing, EmailFailing, RatingDrop, ThumbsDrop, UnansweredSpike,
    ];

    public static readonly IReadOnlyList<string> Support = [VisitorsWaiting, MissedRequests];

    public static bool IsKnown(string kind) => All.Contains(kind, StringComparer.Ordinal);
}

public sealed record Alert(
    string Key,
    string Kind,
    AlertSeverity Severity,
    string Title,
    string Detail,
    int Count,
    DateTimeOffset Since,
    string Link);

public sealed class AlertSnooze
{
    public const int KeyLength = 120;

    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Count { get; set; }
    public AlertSeverity Severity { get; set; }
    public DateTimeOffset? Until { get; set; }
    public string? SnoozedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool Hides(Alert alert, DateTimeOffset now) =>
        (Until is null || Until > now) && alert.Count <= Count && alert.Severity <= Severity;
}

// NotifiedAt survives resolution so the per-kind rate limit still sees it.
public sealed class AlertState
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }

    public bool IsActive => ResolvedAt is null;

    public bool NotifiedForCurrent => NotifiedAt is { } notified && notified >= RaisedAt;
}

internal sealed class AlertSnoozeConfiguration : IEntityTypeConfiguration<AlertSnooze>
{
    public void Configure(EntityTypeBuilder<AlertSnooze> builder)
    {
        builder.ToTable("alert_snoozes");
        builder.HasKey(item => item.Key);
        builder.Property(item => item.Key).HasMaxLength(AlertSnooze.KeyLength);
        builder.Property(item => item.Kind).HasMaxLength(40);
        builder.Property(item => item.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.SnoozedBy).HasMaxLength(200);
    }
}

internal sealed class AlertStateConfiguration : IEntityTypeConfiguration<AlertState>
{
    public void Configure(EntityTypeBuilder<AlertState> builder)
    {
        builder.ToTable("alert_states");
        builder.HasKey(item => item.Key);
        builder.Property(item => item.Key).HasMaxLength(AlertSnooze.KeyLength);
        builder.Property(item => item.Kind).HasMaxLength(40);
        builder.HasIndex(item => item.Kind);
    }
}
