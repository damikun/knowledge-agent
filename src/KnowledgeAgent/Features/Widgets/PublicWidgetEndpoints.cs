using System.Runtime.CompilerServices;
using System.Text.Json;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace KnowledgeAgent.Features.Widgets;

internal static class PublicWidgetEndpoints
{
    public const string RateLimitedCode = "rate_limited";
    public const string DailyCapCode = "daily_cap_reached";
    public const string IdentityRequiredCode = "identity_required";
    public const int FeedbackPerMinute = 20;
    public const long MaxRequestBytes = 64 * 1024;
    public const int AttributeUpdatesPerMinute = 30;
    public const int ServerUpdatesPerMinute = 600;
    public const string ServerTimestampHeader = "X-KA-Timestamp";
    public const string ServerSignatureHeader = "X-KA-Signature";

    private static readonly JsonSerializerOptions ServerJson = new(JsonSerializerDefaults.Web);

    public static void MapPublicWidgetEndpoints(this IEndpointRouteBuilder routes)
    {
        var widget = routes.MapGroup("/api/public/widgets/{key}")
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithMetadata(new RequestBodySizeLimit(MaxRequestBytes));

        widget.MapGet("/config", ConfigAsync).RequireCors(policy => policy.AllowAnyOrigin().WithMethods(HttpMethods.Get));
        widget.MapPost("/sessions", StartSessionAsync);
        widget.MapPost("/messages", SendAsync);
        widget.MapPost("/contact", UpdateContactAsync);
        widget.MapPost("/attributes", UpdateAttributesAsync);
        widget.MapGet("/conversations", ListConversationsAsync);
        widget.MapGet("/conversations/{id:guid}", GetConversationAsync);
        widget.MapGet("/conversations/{id:guid}/events", FollowConversationAsync);
        widget.MapPost("/conversations/{id:guid}/handover", RequestHandoverAsync);
        widget.MapPost("/conversations/{id:guid}/handover/cancel", CancelHandoverAsync);
        widget.MapPost("/conversations/{id:guid}/close", CloseAsync);
        widget.MapPost("/conversations/{id:guid}/feedback", SubmitFeedbackAsync);

        routes.MapPut("/api/public/widgets/{key}/contacts/{userId}", UpsertContactAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestBodySizeLimit(MaxRequestBytes))
            .WithTags("Widget contacts")
            .WithName("UpsertContactFromServer")
            .WithSummary("For the website's backend: creates or updates the contact with this user id. "
                + "Signed with the identity secret: X-KA-Timestamp (Unix seconds) and X-KA-Signature = hex(HMAC-SHA256(secret, \"{timestamp}.{userId}.{body}\")).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<Results<Ok<WidgetPublicConfigDto>, ProblemHttpResult>> ConfigAsync(
        string key,
        HttpResponse response,
        VisitorService visitors,
        LinkHosts links,
        CancellationToken cancellationToken)
    {
        if (await visitors.FindWidgetAsync(key, cancellationToken) is not { } widget)
        {
            return WidgetNotFound();
        }

        response.Headers.CacheControl = "public, max-age=60";
        return TypedResults.Ok(VisitorService.ToConfig(widget) with { LinkHosts = await links.ForWidgetAsync(widget, cancellationToken) });
    }

    private static async Task<IResult> StartSessionAsync(
        string key,
        VisitorSessionRequest? request,
        HttpContext http,
        VisitorService visitors,
        WidgetRateLimiter limiter,
        IOptions<WidgetOptions> options,
        CancellationToken cancellationToken)
    {
        if (await visitors.FindWidgetAsync(key, cancellationToken) is not { } widget)
        {
            return WidgetNotFound();
        }

        if (!limiter.TryAcquire($"sessions:{ClientIp(http)}", options.Value.SessionsPerMinutePerIp, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "Too many new chats from your network. Try again in a minute.");
        }

        try
        {
            return TypedResults.Ok(await visitors.StartSessionAsync(widget, request?.Token, request?.User, cancellationToken, request?.Attributes, request?.Page));
        }
        catch (IdentityRequiredException ex)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sign in first",
                detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = IdentityRequiredCode });
        }
    }

    private static async Task<IResult> SendAsync(
        VisitorContext visitor,
        VisitorMessageRequest request,
        HttpContext http,
        VisitorService visitors,
        IWidgetRepository widgets,
        IAgentRepository agents,
        IChatService chat,
        IHandoverService handover,
        WidgetRateLimiter limiter,
        IOptions<WidgetOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var widget = visitor.Widget;
        var security = widget.Settings.Security;
        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0 && request.Confirmation is { Approved: var approved })
        {
            message = approved ? ChatService.ConfirmText : ChatService.CancelText;
        }

        if (message.Length == 0)
        {
            return ApiResults.Validation("message", "Write a message first.");
        }

        if (message.Length > security.MaxMessageLength)
        {
            return ApiResults.Validation("message", $"Messages can be at most {security.MaxMessageLength} characters long.");
        }

        if (!limiter.TryAcquire($"visitor:{visitor.Visitor.Id}", security.MessagesPerMinute, out var retryAfter)
            || !limiter.TryAcquire($"messages:{ClientIp(http)}", options.Value.MessagesPerMinutePerIp, out retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "You are sending messages too quickly. Wait a moment and try again.");
        }

        var conversation = request.ConversationId is { } conversationId
            ? await visitors.FindOwnConversationAsync(visitor, conversationId, includeTurns: false, cancellationToken)
                ?? throw new ConversationNotFoundException(conversationId)
            : null;

        if (conversation is { Status: ConversationStatus.HandoverPending or ConversationStatus.Human })
        {
            var relayed = await handover.AddVisitorMessageAsync(conversation, visitor.Visitor.Name, message, HandoverService.ViaWidget, cancellationToken);
            return TypedResults.Accepted((string?)null, VisitorService.ToVisitorDto(relayed.ToDto(), widget));
        }

        if (conversation is { Status: ConversationStatus.Closed })
        {
            throw new ConversationClosedException(conversation.Id);
        }

        if (conversation is null && widget.Settings.Behavior.ContactCapture == WidgetChoices.ContactBeforeChat && visitor.Visitor.Email is null)
        {
            return ApiResults.Validation("email", "Leave your email address before you start.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (!visitor.Preview && await widgets.AddDailyMessageAsync(widget.Id, today, cancellationToken) > security.DailyMessageCap)
        {
            var tomorrow = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return TooManyRequests(http, tomorrow - timeProvider.GetUtcNow(), DailyCapCode, "This assistant has answered all it can for today. Please come back tomorrow.");
        }

        if (await agents.GetAsync(widget.AgentId, cancellationToken) is not { } agent)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Not available",
                detail: VisitorErrorTexts.Unavailable);
        }

        var caller = visitor.Preview
            ? WidgetChatCaller.ForPreview(widget, visitor.Visitor)
            : WidgetChatCaller.ForVisitor(widget, visitor.Visitor) with
            {
                ContactId = conversation?.ContactId ?? await visitors.EnsureContactAsync(visitor, cancellationToken),
                Verified = visitor.Verified,
            };
        var stream = chat.StreamAsync(new ChatRequestDto(conversation?.Id, message, Agent: agent.Key, Confirmation: request.Confirmation), caller, cancellationToken);
        var events = ForVisitor(stream, widget.Settings.Behavior.CitationMode, cancellationToken).GetAsyncEnumerator(cancellationToken);
        bool hasFirst;
        try
        {
            hasFirst = await events.MoveNextAsync();
        }
        catch
        {
            await events.DisposeAsync();
            throw;
        }

        return new ChatEventStreamResult(events, hasFirst, loggerFactory.CreateLogger(typeof(ChatEventStreamResult).FullName!));
    }

    private static async Task<IResult> ListConversationsAsync(VisitorContext visitor, VisitorService visitors, CancellationToken cancellationToken) =>
        TypedResults.Ok(await visitors.ListConversationsAsync(visitor, cancellationToken));

    private static async Task<IResult> GetConversationAsync(VisitorConversation conversation, VisitorService visitors, CancellationToken cancellationToken) =>
        TypedResults.Ok(await visitors.GetHistoryAsync(conversation.Visitor, conversation.Id, cancellationToken));

    private static async Task<IResult> UpdateContactAsync(VisitorContext visitor, VisitorContactRequest request, VisitorService visitors, CancellationToken cancellationToken)
    {
        var updated = await visitors.UpdateContactAsync(visitor, request.Name, request.Email, cancellationToken);
        return TypedResults.Ok(new VisitorContactDto(updated.Name, updated.Email));
    }

    private static async Task<IResult> UpdateAttributesAsync(
        VisitorContext visitor,
        VisitorAttributesRequest request,
        HttpContext http,
        VisitorService visitors,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        if (!limiter.TryAcquire($"attributes:{visitor.Visitor.Id}", AttributeUpdatesPerMinute, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "Too many updates at once. Try again in a minute.");
        }

        if (request.Attributes is not { Count: > 0 } attributes)
        {
            return ApiResults.Validation("attributes", "Send at least one attribute.");
        }

        if (visitor.Preview)
        {
            return TypedResults.Ok(new { Applied = Array.Empty<string>(), Rejected = Array.Empty<AttributeRejection>() });
        }

        var result = await visitors.UpdateAttributesAsync(visitor, attributes, cancellationToken);
        return TypedResults.Ok(new { result.Applied, result.Rejected });
    }

    private static async Task<IResult> UpsertContactAsync(
        string key,
        string userId,
        HttpContext http,
        VisitorService visitors,
        WidgetIdentity identity,
        IPeopleRepository people,
        WidgetRateLimiter limiter,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await visitors.FindWidgetAsync(key, cancellationToken) is not { } widget)
        {
            return WidgetNotFound();
        }

        if (widget.ProtectedIdentitySecret is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Identity verification is off",
                detail: "Create an identity secret under Embed code in the widget editor first; it signs these requests.");
        }

        if (!limiter.TryAcquire($"server:{widget.Id}", ServerUpdatesPerMinute, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, $"At most {ServerUpdatesPerMinute} contact updates a minute per widget.");
        }

        var trimmedId = userId.Trim();
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();
        var now = timeProvider.GetUtcNow();
        if (!identity.VerifyServerRequest(widget, userId, http.Request.Headers[ServerTimestampHeader], http.Request.Headers[ServerSignatureHeader], body, now))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid signature",
                detail: $"Sign \"{{timestamp}}.{{userId}}.{{body}}\" with HMAC-SHA256 and the identity secret, and send it as {ServerSignatureHeader} with {ServerTimestampHeader} (Unix seconds, at most 5 minutes off).");
        }

        if (trimmedId.Length is 0 or > Contact.MaxExternalIdLength)
        {
            return ApiResults.Validation("userId", $"The user id must be 1 to {Contact.MaxExternalIdLength} characters.");
        }

        ServerContactRequest? request;
        try
        {
            request = body.Length == 0 ? new ServerContactRequest() : JsonSerializer.Deserialize<ServerContactRequest>(body, ServerJson);
        }
        catch (JsonException)
        {
            return ApiResults.Validation("body", "The body must be JSON: { name, email, attributes }.");
        }

        var errors = new ValidationErrors();
        var name = request?.Name?.Trim();
        var email = request?.Email?.Trim();
        if (name is { Length: > Contact.MaxNameLength })
        {
            errors.Add("name", $"The name can be at most {Contact.MaxNameLength} characters long.");
        }

        if (!string.IsNullOrEmpty(email) && !WidgetEmails.IsValid(email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var result = await people.UpsertFromServerAsync(
            trimmedId,
            string.IsNullOrEmpty(name) ? null : name,
            string.IsNullOrEmpty(email) ? null : email,
            WidgetIdentity.Attributes(request?.Attributes),
            now,
            cancellationToken);
        return TypedResults.Ok(new ServerContactDto(result.ContactId, result.Created, result.Attributes.Applied, result.Attributes.Rejected));
    }

    private static async Task<IResult> RequestHandoverAsync(
        VisitorConversation conversation,
        VisitorHandoverRequest request,
        HttpContext http,
        VisitorService visitors,
        IHandoverService handover,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        var (visitor, behavior) = (conversation.Visitor, conversation.Visitor.Widget.Settings.Behavior);
        if (!behavior.HandoverEnabled)
        {
            return WidgetNotFound();
        }

        if (!limiter.TryAcquire($"handover:{visitor.Visitor.Id}", FeedbackPerMinute, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "Too many requests. Try again in a minute.");
        }

        var contact = await visitors.UpdateContactAsync(visitor, request.Name, request.Email, cancellationToken);
        if (contact.Email is null && behavior.ContactCapture != WidgetChoices.ContactNever)
        {
            return ApiResults.Validation("email", "Leave your email address so the team can reply.");
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Asked for a person" : request.Reason.Trim();
        var (outcome, _) = await handover.RequestAsync(conversation.Id, HandoverSource.Visitor, reason, summary: null, cancellationToken);
        return TypedResults.Ok(new VisitorHandoverDto(outcome.Started ? "handover_pending" : "ai", outcome.Started));
    }

    private static async Task<IResult> CancelHandoverAsync(
        VisitorConversation conversation,
        HttpContext http,
        IHandoverService handover,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        if (!limiter.TryAcquire($"handover:{conversation.Visitor.Visitor.Id}", FeedbackPerMinute, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "Too many requests. Try again in a minute.");
        }

        await handover.CancelAsync(conversation.Id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CloseAsync(VisitorConversation conversation, IHandoverService handover, CancellationToken cancellationToken)
    {
        if (conversation.Conversation.Status != ConversationStatus.Closed)
        {
            await handover.CloseAsync(conversation.Id, conversation.Visitor.Visitor.Name ?? "The visitor", cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> SubmitFeedbackAsync(
        VisitorConversation conversation,
        SubmitFeedbackRequest request,
        HttpContext http,
        IFeedbackService feedback,
        WidgetRateLimiter limiter,
        CancellationToken cancellationToken)
    {
        var visitor = conversation.Visitor;
        if (!visitor.Widget.Settings.Behavior.FeedbackEnabled)
        {
            return WidgetNotFound();
        }

        if (!limiter.TryAcquire($"feedback:{visitor.Visitor.Id}", FeedbackPerMinute, out var retryAfter))
        {
            return TooManyRequests(http, retryAfter, RateLimitedCode, "Too much feedback at once. Try again in a minute.");
        }

        if (visitor.Preview)
        {
            return TypedResults.Ok(new { Id = Guid.Empty, request.MessageId, request.Rating });
        }

        var author = new FeedbackAuthorRef(FeedbackAuthor.Visitor, VisitorService.AuthorKey(visitor));
        var saved = await feedback.SubmitAsync(conversation.Conversation, request, author, cancellationToken);
        return TypedResults.Ok(new { saved.Id, saved.MessageId, saved.Rating });
    }

    private static IResult FollowConversationAsync(
        VisitorConversation conversation,
        int? after,
        HttpContext http,
        VisitorPresence presence,
        ILoggerFactory loggerFactory)
    {
        var stream = new ConversationEventStreamResult(
            conversation.Id,
            ConversationEventStreamResult.AfterSequence(http.Request, after),
            loggerFactory.CreateLogger(typeof(ConversationEventStreamResult).FullName!),
            VisitorService.ViewFor(conversation.Visitor.Widget));
        return new PresenceResult(stream, presence, conversation.Id);
    }

    private static async IAsyncEnumerable<ChatStreamEvent> ForVisitor(
        IAsyncEnumerable<ChatStreamEvent> events,
        string citationMode,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var streamEvent in events.WithCancellation(cancellationToken))
        {
            if (VisitorChatEvents.ToVisitor(streamEvent, citationMode) is { } visible)
            {
                yield return visible;
            }
        }
    }

    internal static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ProblemHttpResult TooManyRequests(HttpContext http, TimeSpan retryAfter, string code, string detail)
    {
        http.Response.Headers[HeaderNames.RetryAfter] = WidgetRateLimiter.RetryAfterSeconds(retryAfter);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests",
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    private static ProblemHttpResult WidgetNotFound() => ApiResults.NotFound("This chat is not available.");
}
