namespace KnowledgeAgent.Features.Widgets;

// Talks to one agent, never the built-in one.
public sealed class Widget
{
    public const int MaxNameLength = 100;
    public const string KeyPrefix = "wk_";

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string PublicKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public Guid AgentId { get; set; }
    public bool Enabled { get; set; } = true;
    public WidgetSettings Settings { get; set; } = new();

    // Encrypted with Data Protection and kept out of Settings, so saving the widget never sends or replaces it.
    public string? ProtectedIdentitySecret { get; set; }
    public DateTimeOffset? IdentitySecretCreatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record WidgetSettings
{
    public WidgetAppearance Appearance { get; init; } = new();
    public WidgetBehavior Behavior { get; init; } = new();
    public WidgetSecurity Security { get; init; } = new();
    public WidgetHandover Handover { get; init; } = new();
    public WidgetConversations Conversations { get; init; } = new();
}

public sealed record WidgetAppearance
{
    public string Title { get; init; } = "Ask us anything";
    public string? Subtitle { get; init; }
    public Guid? AvatarAssetId { get; init; }

    public string LauncherIcon { get; init; } = "message-circle";
    public Guid? LauncherAssetId { get; init; }
    public string BrandColor { get; init; } = "#0f766e";

    public string Theme { get; init; } = WidgetChoices.ThemeAuto;

    public string Preset { get; init; } = WidgetChoices.PresetGlow;

    public WidgetColors Colors { get; init; } = new();
    public string Position { get; init; } = WidgetChoices.PositionRight;
    public string? LauncherLabel { get; init; }

    public bool ShowFooter { get; init; } = true;
}

public sealed record WidgetColors
{
    public Dictionary<string, string> Light { get; init; } = new();
    public Dictionary<string, string> Dark { get; init; } = new();
}

public sealed record WidgetBehavior
{
    public string? Greeting { get; init; } = "Hi! How can I help you today?";
    public List<string> SuggestedQuestions { get; init; } = [];
    public string? Placeholder { get; init; }
    public string CitationMode { get; init; } = WidgetChoices.CitationsTitles;
    public bool FeedbackEnabled { get; init; } = true;

    public bool HandoverEnabled { get; init; }
    public string ContactCapture { get; init; } = WidgetChoices.ContactBeforeHandover;

    public Dictionary<string, string> Texts { get; init; } = new();
}

public sealed record WidgetSecurity
{
    // Sites that may embed the widget, such as https://www.example.com or https://*.example.com. Empty means any site.
    public List<string> AllowedOrigins { get; init; } = [];
    public int MessagesPerMinute { get; init; } = 6;
    public int DailyMessageCap { get; init; } = 500;
    public int MaxMessageLength { get; init; } = 2000;
    public int RetentionDays { get; init; } = 90;

    public bool RequireVerifiedUsers { get; init; }
}

// Older stored settings may still carry offerWhenUnsure; the agent's HandoverPolicy decides when to offer a person.
public sealed record WidgetHandover
{
    public string Channel { get; init; } = WidgetChoices.HandoverPortal;

    public List<string> EmailTo { get; init; } = [];

    public string? SlackChannelId { get; init; }

    public int EscalateAfterMinutes { get; init; } = 10;

    public int WaitMinutes { get; init; } = 15;

    public WidgetAvailability Availability { get; init; } = new();
}

public sealed record WidgetConversations
{
    public bool CloseIdle { get; init; } = true;

    public int AiIdleMinutes { get; init; } = 30;

    // A conversation with a person closes this long after the team wrote last, never while the visitor waits for them.
    public int HumanIdleHours { get; init; } = 24;
}

public sealed record WidgetAvailability
{
    public bool Always { get; init; } = true;
    public string TimeZone { get; init; } = "UTC";
    public List<WidgetOpeningHours> Hours { get; init; } = [];
}

public sealed record WidgetOpeningHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

public static class WidgetChoices
{
    public const string ThemeAuto = "auto";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";
    public const string PresetGlow = "glow";
    public const string PresetGlass = "glass";
    public const string PresetSolid = "solid";
    public const string PositionRight = "right";
    public const string PositionLeft = "left";
    public const string CitationsHidden = "hidden";
    public const string CitationsTitles = "titles";
    public const string CitationsLinks = "links";
    public const string ContactNever = "never";
    public const string ContactBeforeHandover = "before_handover";
    public const string ContactBeforeChat = "before_chat";
    public const string HandoverPortal = "portal";
    public const string HandoverEmail = "email";
    public const string HandoverSlack = "slack";

    public static readonly IReadOnlyList<string> Themes = [ThemeAuto, ThemeLight, ThemeDark];
    public static readonly IReadOnlyList<string> Presets = [PresetGlow, PresetGlass, PresetSolid];

    // Every color of a preset that can be overridden; web/src/widget/shared/theme.ts derives them.
    public static readonly IReadOnlyList<string> ColorTokens =
    [
        "accent", "onAccent", "panel", "text", "muted", "border", "header", "headerText", "userBubble", "userBubbleText",
        "assistantBubble", "assistantBubbleText", "launcher", "launcherIcon", "glow",
    ];
    public static readonly IReadOnlyList<string> Positions = [PositionRight, PositionLeft];
    public static readonly IReadOnlyList<string> CitationModes = [CitationsHidden, CitationsTitles, CitationsLinks];
    public static readonly IReadOnlyList<string> ContactCaptures = [ContactNever, ContactBeforeHandover, ContactBeforeChat];
    public static readonly IReadOnlyList<string> HandoverChannels = [HandoverPortal, HandoverEmail, HandoverSlack];
    public static readonly IReadOnlyList<string> LauncherIcons = ["message-circle", "message-square", "messages-square", "sparkles", "bot", "circle-help", "headset"];

    public static readonly IReadOnlyList<string> TextKeys =
    [
        "launcherLabel", "close", "send", "newChat", "endChat", "talkToPerson", "handoverOffer", "waitingForPerson", "keepChatting", "personJoined",
        "feedbackPrompt", "feedbackThanks", "feedbackComment", "ratingPrompt", "contactTitle", "contactName", "contactEmail",
        "contactSubmit", "sources", "offline", "rateLimited", "dailyCap", "error", "poweredBy", "message", "messages",
        "placeholder", "menu", "thinking", "searching", "helpful", "notHelpful", "skip", "cancel", "dismiss", "ended", "busy",
        "charactersLeft", "you", "team", "rating", "unavailable", "retry", "loading", "newTab", "source",
    ];
}
