using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Widgets;

public interface IWidgetService
{
    Task<IReadOnlyList<WidgetDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<WidgetDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WidgetDto> CreateAsync(SaveWidgetRequest request, CancellationToken cancellationToken = default);
    Task<WidgetDto?> UpdateAsync(Guid id, SaveWidgetRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WidgetAssetDto?> AddAssetAsync(Guid widgetId, byte[] content, CancellationToken cancellationToken = default);

    Task<WidgetIdentitySecretDto?> CreateIdentitySecretAsync(Guid widgetId, CancellationToken cancellationToken = default);

    Task<WidgetDto?> RemoveIdentitySecretAsync(Guid widgetId, CancellationToken cancellationToken = default);
    WidgetOptionsDto GetOptions();
}

internal sealed partial class WidgetService(
    IWidgetRepository widgets,
    IAgentRepository agents,
    WidgetIdentity identity,
    TimeProvider timeProvider,
    ILogger<WidgetService> logger) : IWidgetService
{
    public const int MaxSuggestedQuestions = 4;
    public const int MaxAllowedOrigins = 20;

    public async Task<IReadOnlyList<WidgetDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await widgets.ListAsync(cancellationToken)).Select(widget => widget.ToDto()).ToList();

    public async Task<WidgetDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await widgets.GetAsync(id, cancellationToken))?.ToDto();

    public async Task<WidgetDto> CreateAsync(SaveWidgetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var widget = new Widget { Id = Guid.CreateVersion7(), PublicKey = NewPublicKey(), CreatedAt = now, UpdatedAt = now };
        await ApplyAsync(widget, request, cancellationToken);

        var saved = await widgets.AddAsync(widget, cancellationToken);
        LogCreated(logger, saved.Id, saved.PublicKey);
        return saved.ToDto();
    }

    public async Task<WidgetDto?> UpdateAsync(Guid id, SaveWidgetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await widgets.GetAsync(id, cancellationToken) is not { } widget)
        {
            return null;
        }

        await ApplyAsync(widget, request, cancellationToken);
        widget.UpdatedAt = timeProvider.GetUtcNow();
        await widgets.UpdateAsync(widget, cancellationToken);
        await widgets.DeleteUnusedAssetsAsync(widget.Id, UsedAssets(widget.Settings), cancellationToken);
        LogUpdated(logger, widget.Id, widget.PublicKey);
        return widget.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await widgets.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            LogDeleted(logger, id);
        }

        return deleted;
    }

    public async Task<WidgetAssetDto?> AddAssetAsync(Guid widgetId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (await widgets.GetAsync(widgetId, cancellationToken) is null)
        {
            return null;
        }

        if (content.Length == 0 || content.Length > WidgetAsset.MaxBytes)
        {
            throw new ValidationFailedException("file", $"The image must be at most {WidgetAsset.MaxBytes / 1024} KB.");
        }

        // The content decides the type, never the file name or the declared type; SVG is refused because it can run script.
        var contentType = ImageTypes.Sniff(content)
            ?? throw new ValidationFailedException("file", "Upload a PNG, JPEG or WebP image.");
        var asset = await widgets.AddAssetAsync(
            new WidgetAsset
            {
                Id = Guid.CreateVersion7(),
                WidgetId = widgetId,
                ContentType = contentType,
                Content = content,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);
        return new WidgetAssetDto(asset.Id, WidgetUrls.Media(asset.Id), asset.ContentType, content.Length);
    }

    public async Task<WidgetIdentitySecretDto?> CreateIdentitySecretAsync(Guid widgetId, CancellationToken cancellationToken = default)
    {
        if (await widgets.GetAsync(widgetId, cancellationToken) is not { } widget)
        {
            return null;
        }

        var secret = WidgetIdentity.NewSecret();
        var now = timeProvider.GetUtcNow();
        widget.ProtectedIdentitySecret = identity.Protect(secret);
        widget.IdentitySecretCreatedAt = now;
        widget.UpdatedAt = now;
        await widgets.UpdateAsync(widget, cancellationToken);
        LogIdentitySecretCreated(logger, widget.Id);
        return new WidgetIdentitySecretDto(secret, now);
    }

    public async Task<WidgetDto?> RemoveIdentitySecretAsync(Guid widgetId, CancellationToken cancellationToken = default)
    {
        if (await widgets.GetAsync(widgetId, cancellationToken) is not { } widget)
        {
            return null;
        }

        widget.ProtectedIdentitySecret = null;
        widget.IdentitySecretCreatedAt = null;
        widget.Settings = widget.Settings with { Security = widget.Settings.Security with { RequireVerifiedUsers = false } };
        widget.UpdatedAt = timeProvider.GetUtcNow();
        await widgets.UpdateAsync(widget, cancellationToken);
        LogIdentitySecretRemoved(logger, widget.Id);
        return widget.ToDto();
    }

    public WidgetOptionsDto GetOptions() => new(
        WidgetChoices.LauncherIcons,
        WidgetChoices.Themes,
        WidgetChoices.Presets,
        WidgetChoices.ColorTokens,
        WidgetChoices.Positions,
        WidgetChoices.CitationModes,
        WidgetChoices.ContactCaptures,
        WidgetChoices.HandoverChannels,
        WidgetChoices.TextKeys,
        new WidgetSettings());

    private async Task ApplyAsync(Widget widget, SaveWidgetRequest request, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Widget.MaxNameLength)
        {
            errors.Add("name", $"The name must be 1 to {Widget.MaxNameLength} characters long.");
        }

        var agent = request.AgentId == Guid.Empty ? null : await agents.GetAsync(request.AgentId, cancellationToken);
        if (agent is null)
        {
            errors.Add("agentId", "Choose an agent for the widget.");
        }
        else if (agent.IsBuiltIn)
        {
            // The built-in agent may search every source; a public widget must use an agent scoped for visitors.
            errors.Add("agentId", "The built-in agent is the portal's chat. Create an agent for this widget with the sources visitors may see.");
        }

        var settings = Normalize(request.Settings ?? new WidgetSettings(), errors);
        if (settings.Security.RequireVerifiedUsers && widget.ProtectedIdentitySecret is null)
        {
            errors.Add("settings.security.requireVerifiedUsers", "Turn on identity verification before you require verified users.");
        }

        foreach (var assetId in UsedAssets(settings))
        {
            if (await widgets.GetAssetAsync(assetId, cancellationToken) is not { } asset || asset.WidgetId != widget.Id)
            {
                errors.Add("settings.appearance", $"The image {assetId} was not uploaded for this widget.");
            }
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        widget.Name = name;
        widget.AgentId = request.AgentId;
        widget.Enabled = request.Enabled;
        widget.Settings = settings;
    }

    private static WidgetSettings Normalize(WidgetSettings settings, ValidationErrors errors)
    {
        var appearance = settings.Appearance ?? new WidgetAppearance();
        var behavior = settings.Behavior ?? new WidgetBehavior();
        var security = settings.Security ?? new WidgetSecurity();
        var handover = settings.Handover ?? new WidgetHandover();
        var conversations = settings.Conversations ?? new WidgetConversations();

        appearance = appearance with
        {
            Title = Required(appearance.Title, 60, "settings.appearance.title", errors),
            Subtitle = Optional(appearance.Subtitle, 120, "settings.appearance.subtitle", errors),
            LauncherLabel = Optional(appearance.LauncherLabel, 40, "settings.appearance.launcherLabel", errors),
            LauncherIcon = OneOf(appearance.LauncherIcon, WidgetChoices.LauncherIcons, "settings.appearance.launcherIcon", errors),
            Theme = OneOf(appearance.Theme, WidgetChoices.Themes, "settings.appearance.theme", errors),
            Preset = OneOf(appearance.Preset, WidgetChoices.Presets, "settings.appearance.preset", errors),
            Colors = new WidgetColors
            {
                Light = ColorOverrides(appearance.Colors?.Light, "settings.appearance.colors.light", errors),
                Dark = ColorOverrides(appearance.Colors?.Dark, "settings.appearance.colors.dark", errors),
            },
            Position = OneOf(appearance.Position, WidgetChoices.Positions, "settings.appearance.position", errors),
            BrandColor = Color(appearance.BrandColor, "settings.appearance.brandColor", errors),
        };

        var questions = (behavior.SuggestedQuestions ?? [])
            .Select(question => question?.Trim() ?? string.Empty)
            .Where(question => question.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (questions.Count > MaxSuggestedQuestions || questions.Any(question => question.Length > 120))
        {
            errors.Add("settings.behavior.suggestedQuestions", $"Add at most {MaxSuggestedQuestions} suggested questions of up to 120 characters.");
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in behavior.Texts ?? [])
        {
            if (!WidgetChoices.TextKeys.Contains(key, StringComparer.Ordinal))
            {
                errors.Add("settings.behavior.texts", $"'{key}' is not a widget text. Use one of: {string.Join(", ", WidgetChoices.TextKeys)}.");
            }
            else if (value?.Trim() is { Length: > 0 } text)
            {
                if (text.Length > 200)
                {
                    errors.Add("settings.behavior.texts", $"The text '{key}' must be at most 200 characters long.");
                }

                texts[key] = text;
            }
        }

        behavior = behavior with
        {
            Greeting = Optional(behavior.Greeting, 500, "settings.behavior.greeting", errors),
            Placeholder = Optional(behavior.Placeholder, 80, "settings.behavior.placeholder", errors),
            CitationMode = OneOf(behavior.CitationMode, WidgetChoices.CitationModes, "settings.behavior.citationMode", errors),
            ContactCapture = OneOf(behavior.ContactCapture, WidgetChoices.ContactCaptures, "settings.behavior.contactCapture", errors),
            SuggestedQuestions = questions,
            Texts = texts,
        };

        var origins = new List<string>();
        foreach (var origin in security.AllowedOrigins ?? [])
        {
            if (WidgetOrigins.Normalize(origin) is { } normalized)
            {
                if (!origins.Contains(normalized, StringComparer.Ordinal))
                {
                    origins.Add(normalized);
                }
            }
            else
            {
                errors.Add("settings.security.allowedOrigins", $"'{origin}' is not a site origin such as https://www.example.com or https://*.example.com.");
            }
        }

        if (origins.Count > MaxAllowedOrigins)
        {
            errors.Add("settings.security.allowedOrigins", $"Add at most {MaxAllowedOrigins} sites.");
        }

        security = security with
        {
            AllowedOrigins = origins,
            MessagesPerMinute = Range(security.MessagesPerMinute, 1, 60, "settings.security.messagesPerMinute", errors),
            DailyMessageCap = Range(security.DailyMessageCap, 1, 100_000, "settings.security.dailyMessageCap", errors),
            MaxMessageLength = Range(security.MaxMessageLength, 100, 4000, "settings.security.maxMessageLength", errors),
            RetentionDays = Range(security.RetentionDays, 1, 3650, "settings.security.retentionDays", errors),
        };

        var emails = (handover.EmailTo ?? []).Select(email => email?.Trim() ?? string.Empty).Where(email => email.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (emails.Count > 10 || emails.Any(email => !WidgetEmails.IsValid(email)))
        {
            errors.Add("settings.handover.emailTo", "Add at most 10 valid email addresses.");
        }

        var availability = handover.Availability ?? new WidgetAvailability();
        if (!WidgetSchedule.IsKnownTimeZone(availability.TimeZone))
        {
            errors.Add("settings.handover.availability.timeZone", $"'{availability.TimeZone}' is not a time zone such as Europe/Prague or UTC.");
        }

        if ((availability.Hours ?? []).Any(hours => hours.Closes <= hours.Opens))
        {
            errors.Add("settings.handover.availability.hours", "Opening hours must close after they open.");
        }

        handover = handover with
        {
            Channel = OneOf(handover.Channel, WidgetChoices.HandoverChannels, "settings.handover.channel", errors),
            EmailTo = emails,
            SlackChannelId = Optional(handover.SlackChannelId, 40, "settings.handover.slackChannelId", errors),
            EscalateAfterMinutes = Range(handover.EscalateAfterMinutes, 1, 1440, "settings.handover.escalateAfterMinutes", errors),
            WaitMinutes = Range(handover.WaitMinutes, 1, 1440, "settings.handover.waitMinutes", errors),
            Availability = availability with { Hours = availability.Hours ?? [] },
        };

        conversations = conversations with
        {
            AiIdleMinutes = Range(conversations.AiIdleMinutes, 5, 1440, "settings.conversations.aiIdleMinutes", errors),
            HumanIdleHours = Range(conversations.HumanIdleHours, 1, 168, "settings.conversations.humanIdleHours", errors),
        };

        return new WidgetSettings { Appearance = appearance, Behavior = behavior, Security = security, Handover = handover, Conversations = conversations };
    }

    private static IReadOnlyCollection<Guid> UsedAssets(WidgetSettings settings) =>
        new[] { settings.Appearance.AvatarAssetId, settings.Appearance.LauncherAssetId }.OfType<Guid>().ToList();

    private static string Required(string? value, int maxLength, string field, ValidationErrors errors)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 || trimmed.Length > maxLength)
        {
            errors.Add(field, $"Enter 1 to {maxLength} characters.");
        }

        return trimmed;
    }

    private static string? Optional(string? value, int maxLength, string field, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        if (trimmed is { Length: > 0 } && trimmed.Length > maxLength)
        {
            errors.Add(field, $"Enter at most {maxLength} characters.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string OneOf(string? value, IReadOnlyList<string> choices, string field, ValidationErrors errors)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!choices.Contains(trimmed, StringComparer.Ordinal))
        {
            errors.Add(field, $"Choose one of: {string.Join(", ", choices)}.");
        }

        return trimmed;
    }

    private static int Range(int value, int min, int max, string field, ValidationErrors errors)
    {
        if (value < min || value > max)
        {
            errors.Add(field, $"Enter a number from {min} to {max}.");
        }

        return value;
    }

    private static string Color(string? value, string field, ValidationErrors errors)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!HexColor().IsMatch(trimmed))
        {
            errors.Add(field, "Enter a color such as #0f766e.");
        }

        return trimmed.ToLowerInvariant();
    }

    private static Dictionary<string, string> ColorOverrides(Dictionary<string, string>? colors, string field, ValidationErrors errors)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, value) in colors ?? [])
        {
            if (!WidgetChoices.ColorTokens.Contains(token, StringComparer.Ordinal))
            {
                errors.Add(field, $"'{token}' is not a widget color. Use one of: {string.Join(", ", WidgetChoices.ColorTokens)}.");
            }
            else if (value?.Trim() is { Length: > 0 } color)
            {
                if (!HexColor().IsMatch(color))
                {
                    errors.Add(field, $"Enter the color '{token}' like #0f766e.");
                }

                overrides[token] = color.ToLowerInvariant();
            }
        }

        return overrides;
    }

    // wk_ and 24 random characters: unguessable, so a widget cannot be found by trying keys.
    private static string NewPublicKey()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        return Widget.KeyPrefix + RandomNumberGenerator.GetString(alphabet, 24);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created widget {WidgetId} ({PublicKey})")]
    private static partial void LogCreated(ILogger logger, Guid widgetId, string publicKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated widget {WidgetId} ({PublicKey})")]
    private static partial void LogUpdated(ILogger logger, Guid widgetId, string publicKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created a new identity secret for widget {WidgetId}")]
    private static partial void LogIdentitySecretCreated(ILogger logger, Guid widgetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Turned identity verification off for widget {WidgetId}")]
    private static partial void LogIdentitySecretRemoved(ILogger logger, Guid widgetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted widget {WidgetId}")]
    private static partial void LogDeleted(ILogger logger, Guid widgetId);
}

internal static class WidgetMappings
{
    public static WidgetDto ToDto(this Widget widget) => new(
        widget.Id,
        widget.PublicKey,
        widget.Name,
        widget.AgentId,
        widget.Enabled,
        widget.Settings,
        widget.CreatedAt,
        widget.UpdatedAt)
    {
        IdentityVerification = widget.ProtectedIdentitySecret is not null,
        IdentitySecretCreatedAt = widget.ProtectedIdentitySecret is null ? null : widget.IdentitySecretCreatedAt,
    };
}

internal static class WidgetUrls
{
    public const string Base = "/widget/v1";

    public static string Media(Guid assetId) => $"{Base}/media/{assetId}";
}
