namespace KnowledgeAgent.Features.Chat;

public interface IChatService
{
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequestDto request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequestDto request, ChatCaller caller, CancellationToken cancellationToken = default);

    Task<ConversationDto?> GetConversationAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, CancellationToken cancellationToken = default);
}
