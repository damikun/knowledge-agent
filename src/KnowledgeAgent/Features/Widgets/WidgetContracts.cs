using System.Text.Json;
using KnowledgeAgent.Features.People;

namespace KnowledgeAgent.Features.Widgets;

public sealed record WidgetDto(
    Guid Id,
    string PublicKey,
    string Name,
    Guid AgentId,
    bool Enabled,
    WidgetSettings Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IdentityVerification { get; init; }
    public DateTimeOffset? IdentitySecretCreatedAt { get; init; }
}

public sealed record WidgetIdentitySecretDto(string Secret, DateTimeOffset CreatedAt);

public sealed record SaveWidgetRequest(string Name, Guid AgentId, bool Enabled = true, WidgetSettings? Settings = null);

public sealed record WidgetOptionsDto(
    IReadOnlyList<string> LauncherIcons,
    IReadOnlyList<string> Themes,
    IReadOnlyList<string> Presets,
    IReadOnlyList<string> ColorTokens,
    IReadOnlyList<string> Positions,
    IReadOnlyList<string> CitationModes,
    IReadOnlyList<string> ContactCaptures,
    IReadOnlyList<string> HandoverChannels,
    IReadOnlyList<string> TextKeys,
    WidgetSettings Defaults);

public sealed record WidgetAssetDto(Guid Id, string Url, string ContentType, int Size);

public sealed record WidgetPublicConfigDto(
    string Key,
    string Title,
    string? Subtitle,
    string? AvatarUrl,
    string LauncherIcon,
    string? LauncherIconUrl,
    string BrandColor,
    string Theme,
    string Preset,
    WidgetColors Colors,
    bool ShowFooter,
    string Position,
    string? LauncherLabel,
    string? Greeting,
    IReadOnlyList<string> SuggestedQuestions,
    string? Placeholder,
    string CitationMode,
    bool FeedbackEnabled,
    bool HandoverEnabled,
    string ContactCapture,
    IReadOnlyDictionary<string, string> Texts,
    int MaxMessageLength)
{
    public bool RequireIdentity { get; init; }
}

// Page comes from the loader because the frame cannot read the host page's address.
public sealed record VisitorSessionRequest(
    string? Token = null,
    VisitorUserRequest? User = null,
    Dictionary<string, JsonElement>? Attributes = null,
    VisitorPageRequest? Page = null);

// Url and Referrer are stored without query or fragment, which often carry tokens or personal data.
public sealed record VisitorPageRequest(string? Url = null, string? Referrer = null, string? Language = null);

public sealed record VisitorSessionDto(string Token, Guid VisitorId, Guid? ConversationId, DateTimeOffset ExpiresAt, string? Name, string? Email)
{
    public bool Identified { get; init; }
    public IReadOnlyList<AttributeRejection> RejectedAttributes { get; init; } = [];
}

public sealed record VisitorAttributesRequest(Dictionary<string, JsonElement>? Attributes = null);

public sealed record ServerContactRequest(string? Name = null, string? Email = null, Dictionary<string, JsonElement>? Attributes = null);

public sealed record ServerContactDto(Guid ContactId, bool Created, IReadOnlyList<string> Applied, IReadOnlyList<AttributeRejection> Rejected);

public sealed record WidgetPreviewSessionDto(VisitorSessionDto Session, WidgetPublicConfigDto Config);

public sealed record VisitorMessageRequest(string Message, Guid? ConversationId = null, KnowledgeAgent.Features.Chat.ToolConfirmationRequest? Confirmation = null);

public sealed record VisitorConversationDto(Guid Id, string Status, IReadOnlyList<VisitorMessageDto> Messages, int? Rating = null);

public sealed record VisitorMessageDto(
    Guid Id,
    int Sequence,
    string Author,
    string? AuthorName,
    string Text,
    IReadOnlyList<VisitorCitationDto> Citations,
    DateTimeOffset CreatedAt,
    int? Rating = null);

public sealed record VisitorContactRequest(string? Name = null, string? Email = null);

public sealed record VisitorContactDto(string? Name, string? Email);

public sealed record VisitorHandoverRequest(string? Name = null, string? Email = null, string? Reason = null);

public sealed record VisitorHandoverDto(string Status, bool Started);

public sealed record VisitorCitationDto(string Ref, string Title, string? Url, string? Snippet);
