using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Chat;

internal sealed class ChatService(IConversationRepository conversations, ChatTurnHost turns) : IChatService
{
    public const int MaxMessageLength = 32_000;
    public const string DefaultTitle = "New conversation";
    public const string ConfirmText = "Yes, go ahead.";
    public const string CancelText = "No, cancel that.";

    internal const string InternalErrorMessage = "An unexpected error occurred while answering. Please try again.";
    internal const string PersistErrorMessage = "The answer could not be saved to the conversation history.";
    internal const string ScopeNotFoundMessage =
        "None of the sources this chat is limited to exist any more. Choose other sources or clear the selection.";

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequestDto request, CancellationToken cancellationToken = default) =>
        StreamAsync(request, ChatCaller.Portal, cancellationToken);

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequestDto request, ChatCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        // Validated before the turn starts, so callers can answer 400 before the stream does.
        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0 && request.Confirmation is { } confirmation)
        {
            message = confirmation.Approved ? ConfirmText : CancelText;
        }

        if (request.Confirmation is { CallId: var callId } && (string.IsNullOrWhiteSpace(callId) || callId.Length > 200))
        {
            throw new ValidationFailedException("confirmation", "Name the tool call to confirm.");
        }

        if (message.Length == 0)
        {
            throw new ValidationFailedException("message", "The message must not be empty.");
        }

        if (message.Length > MaxMessageLength)
        {
            throw new ValidationFailedException("message", "The message must be at most 32,000 characters long.");
        }

        return turns.Start(
            request.ConversationId,
            (services, broadcast, turnCancellation) =>
                services.GetRequiredService<ChatTurnExecutor>().RunTurnAsync(request, message, caller, broadcast, turnCancellation),
            cancellationToken);
    }

    public async Task<ConversationDto?> GetConversationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetAsync(id, includeTurns: true, cancellationToken);
        return conversation is null ? null : ToDto(conversation);
    }

    public Task<bool> DeleteConversationAsync(Guid id, CancellationToken cancellationToken = default) =>
        conversations.DeleteAsync(id, cancellationToken);

    internal static ConversationDto ToDto(Conversation conversation) => new(
        conversation.Id,
        conversation.Title,
        conversation.AgentName,
        conversation.PromptName,
        conversation.PromptVersion,
        conversation.PromptOrigin,
        conversation.Profile,
        conversation.SourceIds.ToList(),
        conversation.CreatedAt,
        conversation.UpdatedAt,
        conversation.Turns
            .OrderBy(turn => turn.Sequence)
            .Select(turn => new ConversationTurnDto(
                turn.Id,
                turn.Sequence,
                turn.UserMessage,
                turn.AssistantMessage,
                ChatJson.DeserializeDtos<ToolCallDto>(turn.ToolCallsJson),
                ChatJson.DeserializeDtos<CitationDto>(turn.CitationsJson),
                turn.Provider,
                turn.Model,
                turn.InputTokens,
                turn.OutputTokens,
                turn.FinishReason,
                turn.Error,
                turn.DurationMs,
                turn.CreatedAt,
                turn.TraceId))
            .ToList(),
        conversation.AgentId,
        conversation.Channel.ToString().ToLowerInvariant(),
        ConversationStatuses.ToText(conversation.Status),
        conversation.Messages.OrderBy(message => message.Sequence).Select(message => message.ToDto()).ToList(),
        conversation.WidgetId,
        conversation.VisitorId)
    {
        Pinned = conversation.Pinned,
        ContactId = conversation.ContactId,
        CreatedBy = conversation.CreatedById is { } createdById ? new ConversationCreatorDto(createdById, conversation.CreatedByName ?? createdById) : null,
        Assignee = ConversationAssigneeDto.Of(conversation.AssigneeId, conversation.AssigneeName),
        ActiveAgent = ConversationActiveAgentDto.Of(conversation.ActiveAgentId, conversation.ActiveAgentName),
    };
}

internal static class ConversationStatuses
{
    public static string ToText(ConversationStatus status) => status switch
    {
        ConversationStatus.HandoverPending => "handover_pending",
        _ => status.ToString().ToLowerInvariant(),
    };

    public static ConversationStatus? Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "ai" => ConversationStatus.Ai,
        "handover_pending" => ConversationStatus.HandoverPending,
        "human" => ConversationStatus.Human,
        "closed" => ConversationStatus.Closed,
        _ => null,
    };
}
