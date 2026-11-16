using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KnowledgeAgent.Features.Chat.Live;

internal sealed partial class PostgresLiveBus(NpgsqlDataSource dataSource, LivePayload payloads, ILogger<PostgresLiveBus> logger) : ILiveBus
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
    {
        var payload = payloads.Format(message);
        if (Encoding.UTF8.GetByteCount(payload) > LivePayload.MaxBytes)
        {
            LogPayloadTooLarge(logger, message.ConversationId);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await using var command = dataSource.CreateCommand("SELECT pg_notify($1, $2)");
            command.Parameters.Add(new NpgsqlParameter { Value = LivePayload.Channel });
            command.Parameters.Add(new NpgsqlParameter { Value = payload });
            await command.ExecuteNonQueryAsync(timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogPublishFailed(logger, ex, message.ConversationId);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            LogPublishFailed(logger, ex, message.ConversationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell other instances about conversation {ConversationId}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A live update of conversation {ConversationId} is too large to send; dropped")]
    private static partial void LogPayloadTooLarge(ILogger logger, Guid conversationId);
}

internal sealed partial class PostgresLiveListener(
    NpgsqlDataSource dataSource,
    LivePayload payloads,
    ConversationLiveRelay relay,
    ILogger<PostgresLiveListener> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var received = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var relaying = RelayAsync(received.Reader, stoppingToken);
        try
        {
            await ListenAsync(received.Writer, stoppingToken);
        }
        finally
        {
            received.Writer.TryComplete();
            await relaying;
        }
    }

    private async Task ListenAsync(ChannelWriter<string> received, CancellationToken stoppingToken)
    {
        var recovering = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                connection.Notification += (_, notification) => received.TryWrite(notification.Payload);
                await using (var listen = new NpgsqlCommand($"LISTEN {LivePayload.Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                if (recovering)
                {
                    LogListeningAgain(logger, LivePayload.Channel);
                    recovering = false;
                }
                else
                {
                    LogListening(logger, LivePayload.Channel);
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!await connection.WaitAsync(KeepAliveInterval, stoppingToken))
                    {
                        await using var ping = new NpgsqlCommand("SELECT 1", connection);
                        await ping.ExecuteNonQueryAsync(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                recovering = true;
                LogListenFailed(logger, ex, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task RelayAsync(ChannelReader<string> received, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var payload in received.ReadAllAsync(stoppingToken))
            {
                if (!LivePayload.TryParse(payload, out var message, out var origin))
                {
                    LogUnreadable(logger, payload);
                    continue;
                }

                if (origin != payloads.InstanceId)
                {
                    await relay.RelayAsync(message, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listening for live updates on {Channel}")]
    private static partial void LogListening(ILogger logger, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening for live updates on {Channel} again")]
    private static partial void LogListeningAgain(ILogger logger, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listening for live updates failed; retrying in {Seconds} s")]
    private static partial void LogListenFailed(ILogger logger, Exception exception, double seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped a live update this version cannot read: {Payload}")]
    private static partial void LogUnreadable(ILogger logger, string payload);
}

internal sealed class LivePayload
{
    public const string Channel = "ka_live";

    public const int MaxBytes = 7_999;

    private const string MessagesKind = "message";
    private const string StatusKind = "status";

    public Guid InstanceId { get; } = Guid.NewGuid();

    public string Format(LiveMessage message) => message switch
    {
        MessagesStored stored => Format(MessagesKind, stored.ConversationId, stored.FirstSequence.ToString(CultureInfo.InvariantCulture)),
        StatusChanged changed => Format(StatusKind, changed.ConversationId, ConversationStatuses.ToText(changed.Status)),
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown live message."),
    };

    public static bool TryParse(string? payload, [NotNullWhen(true)] out LiveMessage? message, out Guid origin)
    {
        message = null;
        origin = Guid.Empty;
        var parts = payload?.Split(':', 4);
        if (parts is not { Length: 4 }
            || !Guid.TryParseExact(parts[1], "N", out var conversationId)
            || !Guid.TryParseExact(parts[2], "N", out var sender))
        {
            return false;
        }

        message = parts[0] switch
        {
            MessagesKind when int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var first) && first > 0
                => new MessagesStored(conversationId, first),
            StatusKind when ConversationStatuses.Parse(parts[3]) is { } status => new StatusChanged(conversationId, status),
            _ => null,
        };
        origin = message is null ? Guid.Empty : sender;
        return message is not null;
    }

    private string Format(string kind, Guid conversationId, string value) => $"{kind}:{conversationId:N}:{InstanceId:N}:{value}";
}
