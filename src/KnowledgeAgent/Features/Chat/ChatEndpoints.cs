using System.Security.Claims;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Chat;

internal static class ChatEndpoints
{
    public static RouteHandlerBuilder MapChatEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/chat", ChatAsync)
            .WithTags("Chat")
            .WithName("Chat")
            .WithSummary("Runs one chat turn and streams agent events as text/event-stream.")
            .WithDescription(
                "Each event is written as 'event: <type>' plus 'data: <json>' (camelCase). Types: conversation, agent-started, "
                + "text-delta, reasoning-delta, tool-call, tool-result, usage, agent-completed, error, done.")
            .Produces<ChatStreamEvent>(StatusCodes.Status200OK, ChatEventStreamResult.ContentType)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    public static RouteGroupBuilder MapConversationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/conversations").WithTags("Conversations");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetConversation")
            .WithSummary("A conversation with its turns, tool calls and citations.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteConversation")
            .WithSummary("Delete a conversation.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/stop", (Guid id, ChatTurnHost turns) => TypedResults.Ok(new StopTurnResponse(turns.Stop(id))))
            .WithName("StopAnswer")
            .WithSummary("Stops the answer being written in the conversation and keeps the text so far; stopped is false when no answer was running on this instance.");

        group.MapGet("/{id:guid}/events", EventsAsync)
            .WithName("FollowConversation")
            .WithSummary("Server-sent events of a conversation: its stored messages after Last-Event-ID or ?after=, then new messages, the turn being written and status changes.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Results<ConversationEventStreamResult, ProblemHttpResult>> EventsAsync(
        Guid id,
        int? after,
        HttpRequest request,
        IConversationRepository conversations,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await conversations.GetAsync(id, includeTurns: false, cancellationToken) is null)
        {
            return ApiResults.NotFound($"Conversation '{id}' was not found.");
        }

        return new ConversationEventStreamResult(
            id,
            ConversationEventStreamResult.AfterSequence(request, after),
            loggerFactory.CreateLogger(typeof(ConversationEventStreamResult).FullName!),
            endsAt: SessionCheck.DueAt(request.HttpContext));
    }

    private static async Task<Results<ChatEventStreamResult, ProblemHttpResult>> ChatAsync(
        ChatRequestDto request,
        ClaimsPrincipal user,
        IChatService chat,
        IConversationRepository conversations,
        IWidgetRepository widgets,
        IAgentRepository agents,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // A profile or prompt variables change what the agent is told, so only admins may set them per request.
        if ((request.Profile is not null || request.Variables is { Count: > 0 }) && !user.IsInRole(AuthRoles.Admin))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Admin role required",
                detail: "Only admins can choose the model profile or the prompt variables of a chat.");
        }

        var name = user.FindFirst("name")?.Value ?? user.Identity?.Name;
        var staffId = user.FindFirst("sub")?.Value;
        var caller = new ChatCaller(ConversationChannel.Portal, name, StaffId: staffId);
        var existing = request.ConversationId is { } requestedId && requestedId != Guid.Empty
            ? await conversations.GetAsync(requestedId, includeTurns: false, cancellationToken)
            : null;

        var widgetId = request.ConversationId is { } id && id != Guid.Empty
            ? existing is { Channel: ConversationChannel.Portal } ? existing.WidgetId : null
            : request.WidgetId;
        if (widgetId is { } testedId)
        {
            if (await widgets.GetAsync(testedId, cancellationToken) is not { } widget)
            {
                return ApiResults.NotFound($"Widget '{testedId}' was not found.");
            }

            caller = WidgetChatCaller.ForTest(widget, name, staffId);
            if (existing is null)
            {
                if (await agents.GetAsync(widget.AgentId, cancellationToken) is not { } agent)
                {
                    return ApiResults.NotFound("The agent of this widget no longer exists.");
                }

                // Visitors cannot narrow the scope, so neither does the test.
                request = request with { Agent = agent.Key, SourceIds = null };
            }
        }

        // The turn goes on if the client disconnects.
        var stream = chat.StreamAsync(request, caller, cancellationToken);

        if (request.ConversationId is { } conversationId && conversationId != Guid.Empty && existing is null)
        {
            return ApiResults.NotFound($"Conversation '{conversationId}' was not found.");
        }

        // Read the first event before committing to a 200 event stream, so setup failures still become problem details.
        var events = stream.GetAsyncEnumerator(cancellationToken);
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

    private static async Task<Results<Ok<ConversationDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        IChatService chat,
        IPeopleRepository people,
        CancellationToken cancellationToken)
    {
        var conversation = await chat.GetConversationAsync(id, cancellationToken);
        if (conversation is null)
        {
            return ConversationNotFound(id);
        }

        if (conversation.ContactId is { } contactId && await people.GetAsync(contactId, cancellationToken) is { } contact)
        {
            conversation = conversation with { Contact = new ConversationContactDto(contact.Id, contact.DisplayName, contact.Email, contact.Identified) };
        }

        return TypedResults.Ok(conversation);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        IChatService chat,
        CancellationToken cancellationToken) =>
        await chat.DeleteConversationAsync(id, cancellationToken) ? TypedResults.NoContent() : ConversationNotFound(id);

    private static ProblemHttpResult ConversationNotFound(Guid id) => ApiResults.NotFound($"Conversation '{id}' was not found.");
}
