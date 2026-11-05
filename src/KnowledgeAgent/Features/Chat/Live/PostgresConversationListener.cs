using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KnowledgeAgent.Features.Chat.Live;

internal sealed partial class PostgresConversationListener(
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopeFactory,
    ConversationEventHub hub,
    ConversationNotifications notifications,
    ILogger<PostgresConversationListener> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Relayed one at a time in arrival order: a view skips a message older than the last it showed.
        // Unbounded, since the connection raises notifications synchronously and each is a few bytes.
        var payloads = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var relay = RelayAsync(payloads.Reader, stoppingToken);
        try
        {
            await ListenAsync(payloads.Writer, stoppingToken);
        }
        finally
        {
            payloads.Writer.TryComplete();
            await relay;
        }
    }

    private async Task ListenAsync(ChannelWriter<string> payloads, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                connection.Notification += (_, notification) => payloads.TryWrite(notification.Payload);
                await using (var listen = new NpgsqlCommand($"LISTEN {ConversationNotifications.Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                LogListening(logger, ConversationNotifications.Channel);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Live views on this instance miss other instances' messages until then; they catch up on reconnect.
                LogListenFailed(logger, ex, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task RelayAsync(ChannelReader<string> payloads, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var payload in payloads.ReadAllAsync(stoppingToken))
            {
                await HandleAsync(payload, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        if (!ConversationNotifications.TryParse(payload, out var conversationId, out var afterSequence, out var origin)
            || origin == notifications.InstanceId
            || !hub.HasSubscribers(conversationId))
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            foreach (var message in await conversations.ListMessagesAsync(conversationId, afterSequence - 1, cancellationToken))
            {
                hub.Publish(conversationId, new MessageAddedEvent(message.ToDto()));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRelayFailed(logger, ex, conversationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listening for conversation messages on {Channel}")]
    private static partial void LogListening(ILogger logger, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listening for conversation messages failed; retrying in {Seconds} s")]
    private static partial void LogListenFailed(ILogger logger, Exception exception, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not relay new messages of conversation {ConversationId}")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, Guid conversationId);
}

// Payload: "<conversation>:<first sequence>:<instance>".
internal sealed class ConversationNotifications
{
    public const string Channel = "ka_conversation_messages";

    public Guid InstanceId { get; } = Guid.NewGuid();

    public string Payload(Guid conversationId, int firstSequence) => $"{conversationId:N}:{firstSequence}:{InstanceId:N}";

    public static bool TryParse(string payload, out Guid conversationId, out int firstSequence, out Guid origin)
    {
        conversationId = Guid.Empty;
        firstSequence = 0;
        origin = Guid.Empty;
        var parts = payload.Split(':');
        return parts.Length == 3
            && Guid.TryParseExact(parts[0], "N", out conversationId)
            && int.TryParse(parts[1], out firstSequence)
            && Guid.TryParseExact(parts[2], "N", out origin);
    }
}
