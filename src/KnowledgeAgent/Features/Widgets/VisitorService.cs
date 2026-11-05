using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Widgets;

public sealed record VisitorContext(Widget Widget, WidgetVisitor Visitor, bool Verified = false, bool Preview = false);

internal sealed partial class VisitorService(
    IWidgetRepository widgets,
    IConversationRepository conversations,
    IFeedbackService feedback,
    IPeopleRepository people,
    WidgetIdentity identity,
    VisitorTokens tokens,
    TimeProvider timeProvider,
    ILogger<VisitorService> logger)
{
    public async Task<Widget?> FindWidgetAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 40 || !key.StartsWith(Widget.KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var widget = await widgets.GetByKeyAsync(key, cancellationToken);
        return widget is { Enabled: true } ? widget : null;
    }

    public async Task<Widget?> FindWidgetAsync(string key, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 40 || !key.StartsWith(Widget.KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var widget = await widgets.GetByKeyAsync(key, cancellationToken);
        return widget switch
        {
            { Enabled: true } => widget,
            not null when tokens.Read(token) is { Preview: true } preview && preview.WidgetId == widget.Id => widget,
            _ => null,
        };
    }

    public async Task<WidgetPreviewSessionDto> StartPreviewSessionAsync(Widget widget, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var visitor = await widgets.AddVisitorAsync(
            new WidgetVisitor { Id = Guid.CreateVersion7(), WidgetId = widget.Id, CreatedAt = now, LastSeenAt = now, SessionCount = 1 },
            cancellationToken);
        var (issued, expiresAt) = tokens.Issue(new VisitorIdentity(widget.Id, visitor.Id, Preview: true));
        return new WidgetPreviewSessionDto(new VisitorSessionDto(issued, visitor.Id, null, expiresAt, null, null), ToConfig(widget));
    }

    public static WidgetPublicConfigDto ToConfig(Widget widget)
    {
        var appearance = widget.Settings.Appearance;
        var behavior = widget.Settings.Behavior;
        return new WidgetPublicConfigDto(
            widget.PublicKey,
            appearance.Title,
            appearance.Subtitle,
            appearance.AvatarAssetId is { } avatar ? WidgetUrls.Media(avatar) : null,
            appearance.LauncherIcon,
            appearance.LauncherAssetId is { } launcher ? WidgetUrls.Media(launcher) : null,
            appearance.BrandColor,
            appearance.Theme,
            appearance.Preset,
            appearance.Colors,
            appearance.ShowFooter,
            appearance.Position,
            appearance.LauncherLabel,
            behavior.Greeting,
            behavior.SuggestedQuestions,
            behavior.Placeholder,
            behavior.CitationMode,
            behavior.FeedbackEnabled,
            behavior.HandoverEnabled,
            behavior.ContactCapture,
            behavior.Texts,
            widget.Settings.Security.MaxMessageLength)
        {
            RequireIdentity = widget.Settings.Security.RequireVerifiedUsers && widget.ProtectedIdentitySecret is not null,
        };
    }

    // Signing in as someone else, or a page without the user on a widget that verifies them, starts a new visitor,
    // so nobody sees the previous user's chats on a shared computer.
    public async Task<VisitorSessionDto> StartSessionAsync(
        Widget widget,
        string? token,
        VisitorUserRequest? user,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, JsonElement>? attributes = null,
        VisitorPageRequest? page = null)
    {
        var now = timeProvider.GetUtcNow();
        var verified = identity.Verify(widget, user);
        if (verified is null && widget.Settings.Security.RequireVerifiedUsers && widget.ProtectedIdentitySecret is not null)
        {
            throw new IdentityRequiredException();
        }

        WidgetVisitor? visitor = null;
        // A preview token never resumes anything here: the public gets a visitor of its own.
        var previous = tokens.Read(token) is { Preview: false } read && read.WidgetId == widget.Id ? read : null;
        if (previous is not null)
        {
            visitor = await widgets.GetVisitorAsync(previous.VisitorId, cancellationToken);
        }

        if (visitor is not null && previous!.Verified && widget.ProtectedIdentitySecret is not null)
        {
            var current = visitor.ContactId is { } contactId ? await people.GetAsync(contactId, cancellationToken) : null;
            if (verified is null || current?.ExternalId != verified.UserId)
            {
                visitor = null;
            }
        }

        var visit = VisitOf(page, newSession: visitor is null || now - visitor.LastSeenAt >= VisitorSessions.IdleTime);
        if (visitor is null)
        {
            visitor = await widgets.AddVisitorAsync(
                new WidgetVisitor
                {
                    Id = Guid.CreateVersion7(),
                    WidgetId = widget.Id,
                    CreatedAt = now,
                    LastSeenAt = now,
                    SessionCount = 1,
                    LastPageUrl = visit.PageUrl,
                    LastReferrer = visit.Referrer,
                    Language = visit.Language,
                },
                cancellationToken);
            LogVisitorStarted(logger, visitor.Id, widget.Id);
        }
        else
        {
            visitor.LastSeenAt = now;
            visitor.SessionCount += visit.NewSession ? 1 : 0;
            visitor.LastPageUrl = visit.PageUrl ?? visitor.LastPageUrl;
            visitor.LastReferrer = visit.PageUrl is null ? visitor.LastReferrer : visit.Referrer;
            visitor.Language = visit.Language ?? visitor.Language;
            await widgets.UpdateVisitorAsync(visitor, cancellationToken);
            if (verified is null && visitor.ContactId is { } contactId)
            {
                await people.RecordVisitAsync(contactId, visit, now, cancellationToken);
            }
        }

        var rejected = new List<AttributeRejection>();
        if (verified is not null)
        {
            rejected.AddRange((await people.IdentifyAsync(visitor.Id, verified, now, cancellationToken)).Attributes.Rejected);
            visitor = await widgets.GetVisitorAsync(visitor.Id, cancellationToken) ?? visitor;
        }

        if (attributes is { Count: > 0 })
        {
            var source = verified is not null ? AttributeSources.Verified : AttributeSources.Browser;
            rejected.AddRange((await people.SetVisitorAttributesAsync(visitor.Id, WidgetIdentity.Attributes(new(attributes)), source, now, cancellationToken)).Rejected);
            visitor = await widgets.GetVisitorAsync(visitor.Id, cancellationToken) ?? visitor;
        }

        var (issued, expiresAt) = tokens.Issue(new VisitorIdentity(widget.Id, visitor.Id, verified is not null));
        var conversationId = await conversations.FindLatestForVisitorAsync(visitor.Id, cancellationToken);
        if (conversationId is null && verified is not null && visitor.ContactId is { } identifiedId)
        {
            conversationId = await conversations.FindLatestForContactAsync(identifiedId, widget.Id, cancellationToken);
        }

        return new VisitorSessionDto(issued, visitor.Id, conversationId, expiresAt, visitor.Name, visitor.Email)
        {
            Identified = verified is not null,
            RejectedAttributes = rejected,
        };
    }

    public Task<AttributeChangeResult> UpdateAttributesAsync(VisitorContext context, IReadOnlyDictionary<string, JsonElement> attributes, CancellationToken cancellationToken) =>
        people.SetVisitorAttributesAsync(
            context.Visitor.Id,
            WidgetIdentity.Attributes(new(attributes)),
            context.Verified ? AttributeSources.Verified : AttributeSources.Browser,
            timeProvider.GetUtcNow(),
            cancellationToken);

    internal static VisitorVisit VisitOf(VisitorPageRequest? page, bool newSession) =>
        new(newSession, CleanUrl(page?.Url), CleanUrl(page?.Referrer), CleanLanguage(page?.Language));

    private static string? CleanUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var clean = uri.GetLeftPart(UriPartial.Path);
        return clean.Length > Contact.MaxUrlLength ? clean[..Contact.MaxUrlLength] : clean;
    }

    private static string? CleanLanguage(string? value) =>
        value?.Trim() is { Length: > 1 and <= Contact.MaxLanguageLength } language && language.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && char.IsAsciiLetter(language[0])
            ? language
            : null;

    // A preview token is only accepted with an admin's portal session (asAdmin).
    public async Task<VisitorContext?> AuthenticateAsync(Widget widget, string? token, CancellationToken cancellationToken, bool asAdmin = false)
    {
        if (tokens.Read(token) is not { } identity || identity.WidgetId != widget.Id || (identity.Preview && !asAdmin))
        {
            return null;
        }

        // Tokens issued before the widget required verified users are refused; the frame starts a new session.
        // A preview stands in for a signed-in visitor, as the admin has no account on the website.
        if (!identity.Verified && !identity.Preview && widget.Settings.Security.RequireVerifiedUsers && widget.ProtectedIdentitySecret is not null)
        {
            return null;
        }

        var visitor = await widgets.GetVisitorAsync(identity.VisitorId, cancellationToken);
        return visitor is null
            ? null
            : new VisitorContext(widget, visitor, identity.Verified && widget.ProtectedIdentitySecret is not null, identity.Preview);
    }

    public async Task<Guid> EnsureContactAsync(VisitorContext context, CancellationToken cancellationToken)
    {
        var contactId = await people.EnsureForVisitorAsync(context.Visitor.Id, timeProvider.GetUtcNow(), cancellationToken);
        context.Visitor.ContactId = contactId;
        return contactId;
    }

    // Null when the conversation does not exist or belongs to someone else; both look the same to the visitor.
    public async Task<Conversation?> FindOwnConversationAsync(VisitorContext context, Guid conversationId, bool includeTurns, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns, cancellationToken);
        return conversation is not null && conversation.WidgetId == context.Widget.Id
               && (conversation.VisitorId == context.Visitor.Id
                   || (context.Verified && context.Visitor.ContactId is { } contactId && conversation.ContactId == contactId))
            ? conversation
            : null;
    }

    public async Task<VisitorConversationDto?> GetHistoryAsync(VisitorContext context, Guid conversationId, CancellationToken cancellationToken)
    {
        if (await FindOwnConversationAsync(context, conversationId, includeTurns: false, cancellationToken) is not { } conversation)
        {
            return null;
        }

        // The timeline only: the turns hold model messages a visitor never sees.
        conversation.Messages = (await conversations.ListMessagesAsync(conversationId, cancellationToken: cancellationToken)).ToList();
        var ratings = await feedback.RatingsByAsync(conversationId, AuthorKey(context), cancellationToken);
        var history = ToVisitorDto(conversation, context.Widget);
        return history with
        {
            Messages = history.Messages.Select(message => message with { Rating = ratings.TryGetValue(message.Id, out var rating) ? rating : null }).ToList(),
            Rating = ratings.TryGetValue(Guid.Empty, out var conversationRating) ? conversationRating : null,
        };
    }

    public static string AuthorKey(VisitorContext context) => context.Visitor.Id.ToString();

    public async Task<WidgetVisitor> UpdateContactAsync(VisitorContext context, string? name, string? email, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var trimmedName = name?.Trim();
        var trimmedEmail = email?.Trim();
        if (trimmedName is { Length: > WidgetVisitor.MaxNameLength })
        {
            errors.Add("name", $"The name can be at most {WidgetVisitor.MaxNameLength} characters long.");
        }

        if (!string.IsNullOrEmpty(trimmedEmail) && !WidgetEmails.IsValid(trimmedEmail))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var visitor = context.Visitor;
        visitor.Name = string.IsNullOrEmpty(trimmedName) ? visitor.Name : trimmedName;
        visitor.Email = string.IsNullOrEmpty(trimmedEmail) ? visitor.Email : trimmedEmail;
        visitor.LastSeenAt = timeProvider.GetUtcNow();
        await widgets.UpdateVisitorAsync(visitor, cancellationToken);

        if (!context.Preview && (!string.IsNullOrEmpty(trimmedName) || !string.IsNullOrEmpty(trimmedEmail)))
        {
            await people.SaveVisitorDetailsAsync(visitor.Id, trimmedName, trimmedEmail, visitor.LastSeenAt, cancellationToken);
        }

        return visitor;
    }

    public static VisitorConversationDto ToVisitorDto(Conversation conversation, Widget widget) => new(
        conversation.Id,
        ConversationStatuses.ToText(conversation.Status),
        conversation.Messages
            .OrderBy(message => message.Sequence)
            .Select(message => ToVisitorDto(message.ToDto(), widget))
            .OfType<VisitorMessageDto>()
            .ToList());

    public static VisitorMessageDto? ToVisitorDto(ConversationMessageDto message, Widget widget)
    {
        if (message.StaffOnly)
        {
            return null;
        }

        var citationMode = widget.Settings.Behavior.CitationMode;

        // Widget turns store their failure in visitor words; anything else from a failed turn stays internal.
        var text = message.Author == "system" && message.TurnId is not null && !VisitorErrorTexts.All.Contains(message.Text)
            ? VisitorErrorTexts.Failed
            : message.Text;
        return new VisitorMessageDto(
            message.Id,
            message.Sequence,
            message.Author == "user" ? "visitor" : message.Author,
            message.Author == "staff" ? message.AuthorName : null,
            text,
            message.Citations.Select(citation => VisitorChatEvents.ToVisitorCitation(citation, citationMode)).OfType<VisitorCitationDto>().ToList(),
            message.CreatedAt);
    }

    public static ConversationEventView ViewFor(Widget widget) => new(
        message => ToVisitorDto(message, widget),
        turn => VisitorChatEvents.ToVisitor(turn, widget.Settings.Behavior.CitationMode));

    [LoggerMessage(Level = LogLevel.Information, Message = "Visitor {VisitorId} started on widget {WidgetId}")]
    private static partial void LogVisitorStarted(ILogger logger, Guid visitorId, Guid widgetId);
}
