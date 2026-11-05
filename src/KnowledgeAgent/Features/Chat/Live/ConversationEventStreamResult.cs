using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace KnowledgeAgent.Features.Chat.Live;

// SSE events: "message" (id = sequence, so Last-Event-ID resumes), "turn" and "conversation".
// A comment every 15 s keeps proxies from closing idle streams. Staff streams end at their next session check.
internal sealed record ConversationEventView(Func<ConversationMessageDto, object?> Message, Func<ChatStreamEvent, ChatStreamEvent?> Turn)
{
    public static readonly ConversationEventView Staff = new(message => message, turn => turn);
}

internal sealed partial class ConversationEventStreamResult(
    Guid conversationId,
    int afterSequence,
    ILogger logger,
    ConversationEventView? view = null,
    DateTimeOffset? endsAt = null) : IResult
{
    private readonly ConversationEventView _view = view ?? ConversationEventView.Staff;

    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var hub = services.GetRequiredService<ConversationEventHub>();
        var turns = services.GetRequiredService<ChatTurnHost>();
        var conversations = services.GetRequiredService<IConversationRepository>();
        var serializerOptions = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;

        // Subscribed before reading the stored messages, so nothing stored in between is missed.
        using var subscription = hub.Subscribe(conversationId);
        try
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = ChatEventStreamResult.ContentType;
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await response.StartAsync(cancellationToken);

            var last = afterSequence;
            foreach (var message in await conversations.ListMessagesAsync(conversationId, afterSequence, cancellationToken))
            {
                WriteMessage(response.BodyWriter, message.ToDto(), serializerOptions);
                last = message.Sequence;
            }

            var turnKey = Guid.Empty;
            var turnIndex = -1;
            if (turns.Find(conversationId) is { } active)
            {
                foreach (var past in active.Snapshot())
                {
                    WriteTurn(response.BodyWriter, past, serializerOptions);
                    turnIndex++;
                }

                turnKey = active.Key;
            }

            await response.BodyWriter.FlushAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var wait = HeartbeatInterval;
                if (endsAt is { } end)
                {
                    var left = end - timeProvider.GetUtcNow();
                    if (left <= TimeSpan.Zero)
                    {
                        LogEnded(logger, conversationId);
                        return;
                    }

                    wait = left < wait ? left : wait;
                }

                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeat.CancelAfter(wait);
                ConversationLiveEvent liveEvent;
                try
                {
                    liveEvent = await subscription.Reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    response.BodyWriter.Write(": ping\n\n"u8);
                    await response.BodyWriter.FlushAsync(cancellationToken);
                    continue;
                }

                switch (liveEvent)
                {
                    case MessageAddedEvent added when added.Message.Sequence > last:
                        WriteMessage(response.BodyWriter, added.Message, serializerOptions);
                        last = added.Message.Sequence;
                        break;
                    case TurnStreamEvent turn when turn.TurnKey != turnKey || turn.Index > turnIndex:
                        WriteTurn(response.BodyWriter, turn.Event, serializerOptions);
                        turnKey = turn.TurnKey;
                        turnIndex = turn.Index;
                        break;
                    case ConversationChangedEvent changed:
                        WriteEvent(response.BodyWriter, "conversation", null, new { conversationId = changed.ConversationId, status = changed.Status }, serializerOptions);
                        break;
                    default:
                        continue;
                }

                var flush = await response.BodyWriter.FlushAsync(cancellationToken);
                if (flush.IsCompleted || flush.IsCanceled)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogClosed(logger, conversationId);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            LogFailed(logger, ex, conversationId);
        }
    }

    public static int AfterSequence(HttpRequest request, int? after) =>
        int.TryParse(request.Headers["Last-Event-ID"], NumberStyles.None, CultureInfo.InvariantCulture, out var lastEventId)
            ? lastEventId
            : Math.Max(0, after ?? 0);

    private void WriteTurn(PipeWriter writer, ChatStreamEvent streamEvent, JsonSerializerOptions serializerOptions)
    {
        if (_view.Turn(streamEvent) is { } visible)
        {
            WriteEvent(writer, "turn", null, new { type = visible.EventType, data = (object)visible }, serializerOptions);
        }
    }

    private void WriteMessage(PipeWriter writer, ConversationMessageDto message, JsonSerializerOptions serializerOptions)
    {
        if (_view.Message(message) is { } visible)
        {
            WriteEvent(writer, "message", message.Sequence.ToString(CultureInfo.InvariantCulture), visible, serializerOptions);
        }
    }

    private static void WriteEvent(PipeWriter writer, string eventType, string? id, object payload, JsonSerializerOptions serializerOptions)
    {
        if (id is not null)
        {
            writer.Write("id: "u8);
            writer.Write(Encoding.UTF8.GetBytes(id));
            writer.Write("\n"u8);
        }

        writer.Write("event: "u8);
        writer.Write(Encoding.UTF8.GetBytes(eventType));
        writer.Write("\ndata: "u8);
        using (var json = new Utf8JsonWriter(writer, WriterOptions))
        {
            JsonSerializer.Serialize(json, payload, payload.GetType(), serializerOptions);
        }

        writer.Write("\n\n"u8);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Live view of conversation {ConversationId} closed by the client")]
    private static partial void LogClosed(ILogger logger, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Live view of conversation {ConversationId} ended for the session check; the client reconnects")]
    private static partial void LogEnded(ILogger logger, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live view of conversation {ConversationId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid conversationId);
}
