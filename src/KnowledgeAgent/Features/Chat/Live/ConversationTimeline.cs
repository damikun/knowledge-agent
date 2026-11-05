using Microsoft.Extensions.Logging;
using Npgsql;

namespace KnowledgeAgent.Features.Chat.Live;

public interface IConversationNotifier
{
    Task NotifyAsync(Guid conversationId, int firstSequence, CancellationToken cancellationToken = default);
}

internal sealed class NullConversationNotifier : IConversationNotifier
{
    public Task NotifyAsync(Guid conversationId, int firstSequence, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class PostgresConversationNotifier(NpgsqlDataSource dataSource, ConversationNotifications notifications) : IConversationNotifier
{
    public async Task NotifyAsync(Guid conversationId, int firstSequence, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("SELECT pg_notify($1, $2)");
        command.Parameters.Add(new NpgsqlParameter { Value = ConversationNotifications.Channel });
        command.Parameters.Add(new NpgsqlParameter { Value = notifications.Payload(conversationId, firstSequence) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed partial class ConversationTimeline(
    IConversationRepository conversations,
    ConversationEventHub hub,
    IConversationNotifier notifier,
    ILogger<ConversationTimeline> logger)
{
    public async Task<ConversationMessage> AddAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        var saved = await conversations.AddMessageAsync(message, cancellationToken);
        hub.Publish(saved.ConversationId, new MessageAddedEvent(saved.ToDto()));

        // A missed notification only delays other instances' views until they reconnect, so it never fails the write.
        try
        {
            await notifier.NotifyAsync(saved.ConversationId, saved.Sequence, CancellationToken.None);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            LogNotifyFailed(logger, ex, saved.ConversationId);
        }

        return saved;
    }

    public async Task SetStatusAsync(Guid conversationId, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        await conversations.UpdateStatusAsync(conversationId, status, cancellationToken);
        hub.Publish(conversationId, new ConversationChangedEvent(conversationId, ConversationStatuses.ToText(status)));
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

        hub.Publish(conversationId, new ConversationChangedEvent(conversationId, ConversationStatuses.ToText(status)));
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell other instances about new messages in conversation {ConversationId}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception, Guid conversationId);
}
