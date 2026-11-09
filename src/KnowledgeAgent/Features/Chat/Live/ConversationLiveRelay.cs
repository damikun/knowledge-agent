using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Chat.Live;

// Turns what another instance published into events for the views on this one.
internal sealed partial class ConversationLiveRelay(
    IServiceScopeFactory scopeFactory,
    ConversationEventHub hub,
    ILogger<ConversationLiveRelay> logger)
{
    public async Task RelayAsync(LiveMessage message, CancellationToken cancellationToken)
    {
        if (!hub.HasSubscribers(message.ConversationId))
        {
            return;
        }

        try
        {
            switch (message)
            {
                case MessagesStored stored:
                    await using (var scope = scopeFactory.CreateAsyncScope())
                    {
                        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
                        var after = stored.FirstSequence - 1;
                        foreach (var saved in await conversations.ListMessagesAsync(stored.ConversationId, after, cancellationToken))
                        {
                            hub.Publish(stored.ConversationId, new MessageAddedEvent(saved.ToDto()));
                        }
                    }

                    break;
                case StatusChanged changed:
                    hub.Publish(changed.ConversationId, new ConversationChangedEvent(changed.ConversationId, ConversationStatuses.ToText(changed.Status)));
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRelayFailed(logger, ex, message.ConversationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not relay a live update of conversation {ConversationId}")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, Guid conversationId);
}
