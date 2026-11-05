using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Handover;

internal sealed partial class SlackInbound(
    IHandoverRepository handovers,
    IConversationRepository conversations,
    IHandoverService handover,
    ISlackGateway slack,
    TimeProvider timeProvider,
    ILogger<SlackInbound> logger)
{
    public async Task HandleEventAsync(JsonNode payload, CancellationToken cancellationToken)
    {
        var item = payload["event"];
        if (item?["type"]?.GetValue<string>() != "message"
            || item["bot_id"] is not null
            || item["subtype"]?.GetValue<string>() is { } subtype && subtype != "thread_broadcast"
            || item["thread_ts"]?.GetValue<string>() is not { } threadTs
            || item["channel"]?.GetValue<string>() is not { } channel
            || item["user"]?.GetValue<string>() is not { } userId
            || item["text"]?.GetValue<string>() is not { Length: > 0 } text)
        {
            return;
        }

        if (await handovers.FindBySlackThreadAsync(channel, threadTs, cancellationToken) is not { } found)
        {
            return;
        }

        var eventId = payload["event_id"]?.GetValue<string>() ?? $"{channel}:{item["ts"]?.GetValue<string>()}";
        if (!await handovers.TryMarkInboundAsync("slack:" + eventId, timeProvider.GetUtcNow(), cancellationToken))
        {
            return;
        }

        var name = await slack.GetUserNameAsync(userId, cancellationToken) ?? "Support";
        await handover.AddStaffMessageAsync(found.ConversationId, name, SlackMessages.Unescape(text), HandoverService.ViaSlack, cancellationToken);
        LogReply(logger, found.Id);
    }

    public async Task HandleInteractionAsync(JsonNode payload, CancellationToken cancellationToken)
    {
        if (payload["type"]?.GetValue<string>() != "block_actions"
            || payload["actions"]?[0] is not { } action
            || !Guid.TryParse(action["value"]?.GetValue<string>(), out var handoverId)
            || await handovers.GetAsync(handoverId, cancellationToken) is not { } found
            || await conversations.GetAsync(found.ConversationId, includeTurns: false, cancellationToken) is null)
        {
            return;
        }

        var userId = payload["user"]?["id"]?.GetValue<string>();
        var name = (userId is null ? null : await slack.GetUserNameAsync(userId, cancellationToken))
            ?? payload["user"]?["name"]?.GetValue<string>()
            ?? "Support";
        switch (action["action_id"]?.GetValue<string>())
        {
            case SlackMessages.TakeAction:
                await handover.JoinAsync(found.ConversationId, name, cancellationToken);
                break;
            case SlackMessages.ReturnAction:
                await handover.ReturnToAgentAsync(found.ConversationId, name, cancellationToken);
                break;
            case SlackMessages.CloseAction:
                await handover.CloseAsync(found.ConversationId, name, cancellationToken);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A Slack reply was added to handover {HandoverId}")]
    private static partial void LogReply(ILogger logger, Guid handoverId);
}

// Socket Mode needs no public URL or signing secret. Slack wants an ack within three seconds, so ack first, then handle.
internal sealed partial class SlackSocketModeService(
    IIntegrationSettings integrations,
    ISlackGateway slack,
    IServiceScopeFactory scopes,
    ILogger<SlackSocketModeService> logger) : BackgroundService, ISlackConnectionState
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    private volatile bool _connected;
    private CancellationTokenSource _settingsChanged = new();

    public bool IsConnected => _connected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        integrations.Changed += OnSettingsChanged;
        var backoff = TimeSpan.FromSeconds(1);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var changed = Volatile.Read(ref _settingsChanged);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, changed.Token);
                try
                {
                    var settings = await integrations.GetSlackAsync(stoppingToken);
                    if (!settings.CanReceive)
                    {
                        await Task.Delay(IdleCheck, linked.Token);
                        continue;
                    }

                    await RunConnectionAsync(linked.Token);
                    backoff = TimeSpan.FromSeconds(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) when (changed.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LogConnectionFailed(logger, ex, backoff.TotalSeconds);
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                }
                finally
                {
                    _connected = false;
                }
            }
        }
        finally
        {
            integrations.Changed -= OnSettingsChanged;
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        var url = await slack.OpenSocketAsync(cancellationToken);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await socket.ConnectAsync(url, cancellationToken);
        _connected = true;
        LogConnected(logger);

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var json = await ReceiveAsync(socket, cancellationToken);
            if (json is null)
            {
                return;
            }

            var envelope = JsonNode.Parse(json);
            if (envelope?["envelope_id"]?.GetValue<string>() is { } envelopeId)
            {
                var ack = Encoding.UTF8.GetBytes(new JsonObject { ["envelope_id"] = envelopeId }.ToJsonString());
                await socket.SendAsync(ack, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }

            switch (envelope?["type"]?.GetValue<string>())
            {
                case "disconnect":
                    // Slack rotates connections every few hours and says so first.
                    return;
                case "events_api" when envelope["payload"] is { } payload:
                    _ = DispatchAsync((inbound, token) => inbound.HandleEventAsync(payload, token), cancellationToken);
                    break;
                case "interactive" when envelope["payload"] is { } payload:
                    _ = DispatchAsync((inbound, token) => inbound.HandleInteractionAsync(payload, token), cancellationToken);
                    break;
            }
        }
    }

    private async Task DispatchAsync(Func<SlackInbound, CancellationToken, Task> handle, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await handle(scope.ServiceProvider.GetRequiredService<SlackInbound>(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHandlingFailed(logger, ex);
        }
    }

    private static async Task<string?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
    }

    private void OnSettingsChanged()
    {
        // Not disposed: the connection loop may still be reading its token.
        Interlocked.Exchange(ref _settingsChanged, new CancellationTokenSource()).Cancel();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to Slack in Socket Mode")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Slack connection failed; trying again in {Seconds} s")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Slack event could not be handled")]
    private static partial void LogHandlingFailed(ILogger logger, Exception exception);
}
