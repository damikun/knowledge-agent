namespace KnowledgeAgent.Features.Alerts;

public sealed record AlertDto(
    string Key,
    string Kind,
    AlertSeverity Severity,
    string Title,
    string Detail,
    int Count,
    DateTimeOffset Since,
    string Link);

public sealed record AlertListDto(IReadOnlyList<AlertDto> Alerts, int Snoozed);

public sealed record SnoozeAlertRequest(int Hours);

public sealed record AlertSettingsDto(
    bool Slack,
    bool Email,
    IReadOnlyList<string> Kinds,
    int WaitingMinutes,
    bool SlackReady,
    bool EmailReady,
    string? SlackChannel,
    string? EmailTo,
    IReadOnlyList<string> AllKinds);

public sealed record SaveAlertSettingsRequest(bool Slack, bool Email, IReadOnlyList<string>? Kinds, int? WaitingMinutes);
