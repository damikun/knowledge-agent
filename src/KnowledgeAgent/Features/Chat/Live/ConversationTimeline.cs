using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Chat.Live;

public sealed partial class ConversationTimeline(
    IConversationRepository conversations,
    ConversationEventHub hub,
    ILiveBus bus,
    ILogger<ConversationTimeline> logger)
{
    public async Task<ConversationMessage> AddAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        var saved = await conversations.AddMessageAsync(message, cancellationToken);
        hub.Publish(saved.ConversationId, new MessageAddedEvent(saved.ToDto()));
        await PublishAsync(new MessagesStored(saved.ConversationId, saved.Sequence));
        return saved;
    }

    public async Task SetStatusAsync(Guid conversationId, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        await conversations.UpdateStatusAsync(conversationId, status, cancellationToken);
        await StatusChangedAsync(conversationId, status);
    }

    public async Task<bool> TrySetStatusAsync(
        Guid conversationId,
        ConversationStatus expected,
        ConversationStatus status,
        DateTimeOffset? updatedBefore = null,
        CancellationToken cancellationToken = default)
    {
        if (!await conversations.TryUpdateStatusAsync(conversationId, expected, status, updatedBefore, cancellationToken))
        {
            return false;
        }

        await StatusChangedAsync(conversationId, status);
        return true;
    }

    private async Task StatusChangedAsync(Guid conversationId, ConversationStatus status)
    {
        hub.Publish(conversationId, new ConversationChangedEvent(conversationId, ConversationStatuses.ToText(status)));
        await PublishAsync(new StatusChanged(conversationId, status));
    }

    // The change is stored already: a missed update only delays other instances' views until they reconnect,
    // so it never fails the write, and the caller giving up does not stop it.
    private async Task PublishAsync(LiveMessage message)
    {
        try
        {
            await bus.PublishAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogPublishFailed(logger, ex, message.ConversationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell other instances about conversation {ConversationId}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid conversationId);
}
