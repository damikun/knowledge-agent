using System.Diagnostics.CodeAnalysis;

namespace KnowledgeAgent.Features.Integrations;

public sealed record EmailSettings
{
    public bool Enabled { get; init; }
    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;

    public string SmtpSecurity { get; init; } = MailSecurity.Auto;
    public string? SmtpUser { get; init; }
    public string? SmtpPassword { get; init; }

    public string? FromAddress { get; init; }
    public string? FromName { get; init; }

    public string? SupportAddress { get; init; }

    public string? ReplyAddress { get; init; }
    public bool PlusAddressing { get; init; } = true;

    public string? ImapHost { get; init; }
    public int ImapPort { get; init; } = 993;
    public string ImapSecurity { get; init; } = MailSecurity.Auto;
    public string? ImapUser { get; init; }
    public string? ImapPassword { get; init; }

    public string ProcessedFolder { get; init; } = "Processed";

    // Only these senders count as staff replies; empty trusts the support address's domain.
    public List<string> StaffAddresses { get; init; } = [];
    public List<string> StaffDomains { get; init; } = [];

    // Legacy: only read when neither the saved public address nor App:PublicUrl is set.
    public string? PortalUrl { get; init; }

    [MemberNotNullWhen(true, nameof(SmtpHost), nameof(FromAddress))]
    public bool CanSend => Enabled && !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(FromAddress);

    [MemberNotNullWhen(true, nameof(ImapHost), nameof(ImapUser), nameof(ReplyAddress))]
    public bool CanReceive => Enabled && !string.IsNullOrWhiteSpace(ImapHost) && !string.IsNullOrWhiteSpace(ImapUser) && !string.IsNullOrWhiteSpace(ReplyAddress);
}

public sealed record SlackSettings
{
    public bool Enabled { get; init; }

    public string? BotToken { get; init; }

    // xapp- app-level token with the connections:write scope, for Socket Mode.
    public string? AppToken { get; init; }

    public string? ChannelId { get; init; }

    [MemberNotNullWhen(true, nameof(BotToken))]
    public bool CanPost => Enabled && !string.IsNullOrWhiteSpace(BotToken);

    [MemberNotNullWhen(true, nameof(BotToken), nameof(AppToken))]
    public bool CanReceive => CanPost && !string.IsNullOrWhiteSpace(AppToken);
}

public static class MailSecurity
{
    public const string Auto = "auto";
    public const string None = "none";
    public const string StartTls = "starttls";
    public const string Ssl = "ssl";

    public static readonly IReadOnlyList<string> All = [Auto, None, StartTls, Ssl];
}

// Secrets are never returned, only whether they are set.
public sealed record EmailSettingsDto(
    bool Enabled,
    string? SmtpHost,
    int SmtpPort,
    string SmtpSecurity,
    string? SmtpUser,
    bool HasSmtpPassword,
    string? FromAddress,
    string? FromName,
    string? SupportAddress,
    string? ReplyAddress,
    bool PlusAddressing,
    string? ImapHost,
    int ImapPort,
    string ImapSecurity,
    string? ImapUser,
    bool HasImapPassword,
    string ProcessedFolder,
    IReadOnlyList<string> StaffAddresses,
    IReadOnlyList<string> StaffDomains,
    string? PortalUrl,
    bool CanSend,
    bool CanReceive,
    bool Saved);

public sealed record SlackSettingsDto(bool Enabled, bool HasBotToken, bool HasAppToken, string? ChannelId, bool CanPost, bool CanReceive, bool Saved, bool Connected);

public sealed record IntegrationsDto(EmailSettingsDto Email, SlackSettingsDto Slack);

// Secrets: null keeps the saved value, an empty string removes it.
public sealed record SaveEmailSettingsRequest(
    bool Enabled,
    string? SmtpHost,
    int SmtpPort,
    string? SmtpSecurity,
    string? SmtpUser,
    string? SmtpPassword,
    string? FromAddress,
    string? FromName,
    string? SupportAddress,
    string? ReplyAddress,
    bool PlusAddressing,
    string? ImapHost,
    int ImapPort,
    string? ImapSecurity,
    string? ImapUser,
    string? ImapPassword,
    string? ProcessedFolder,
    IReadOnlyList<string>? StaffAddresses,
    IReadOnlyList<string>? StaffDomains,
    string? PortalUrl);

public sealed record SaveSlackSettingsRequest(bool Enabled, string? BotToken, string? AppToken, string? ChannelId);

public sealed record IntegrationTestResultDto(bool Ok, string Message, IReadOnlyList<string>? Details = null);

public sealed record TestEmailRequest(string? To);
