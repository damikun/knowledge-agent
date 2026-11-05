namespace KnowledgeAgent.Features.Chat;

public interface IConversationRepository
{
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task<Conversation?> GetAsync(Guid id, bool includeTurns = true, CancellationToken cancellationToken = default);

    Task<Guid?> FindLatestForVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default);

    Task<Guid?> FindLatestForContactAsync(Guid contactId, Guid widgetId, CancellationToken cancellationToken = default);

    Task<int> DeleteWidgetConversationsAsync(Guid widgetId, DateTimeOffset updatedBefore, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListIdleAsync(
        Guid widgetId,
        ConversationStatus status,
        DateTimeOffset updatedBefore,
        IReadOnlyCollection<MessageAuthor>? lastAuthors,
        int take,
        CancellationToken cancellationToken = default);

    Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default);

    Task<ConversationMessage> AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid conversationId, int afterSequence = 0, CancellationToken cancellationToken = default);

    Task<Guid?> FindConversationOfMessageAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, ConversationStatus status, CancellationToken cancellationToken = default);

    Task<bool> TryUpdateStatusAsync(
        Guid id,
        ConversationStatus expected,
        ConversationStatus status,
        DateTimeOffset? updatedBefore = null,
        CancellationToken cancellationToken = default);

    Task UpdateTitleAsync(Guid id, string title, CancellationToken cancellationToken = default);

    Task UpdateTurnToolCallsAsync(Guid turnId, string toolCallsJson, CancellationToken cancellationToken = default);

    Task SetActiveAgentAsync(Guid id, Guid? agentId, string? agentName, CancellationToken cancellationToken = default);

    Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
