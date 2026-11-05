using System.Buffers;
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

namespace KnowledgeAgent.Features.Chat;

internal sealed partial class ChatEventStreamResult(IAsyncEnumerator<ChatStreamEvent> events, bool hasCurrent, ILogger logger) : IResult
{
    public const string ContentType = "text/event-stream";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // The relaxed encoder is safe because SSE is not HTML, and it still escapes newlines that would split a data: line.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var cancellationToken = httpContext.RequestAborted;
        var serializerOptions = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var eventCount = 0;

        try
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = ContentType;
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await response.StartAsync(cancellationToken);

            var hasEvent = hasCurrent;
            while (hasEvent)
            {
                WriteEvent(response.BodyWriter, events.Current, serializerOptions);
                eventCount++;
                var flush = await response.BodyWriter.FlushAsync(cancellationToken);
                if (flush.IsCompleted || flush.IsCanceled)
                {
                    LogClientDisconnected(logger, eventCount);
                    return;
                }

                hasEvent = await events.MoveNextAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogClientDisconnected(logger, eventCount);
        }
        catch (Exception exception)
        {
            LogStreamFailed(logger, exception, eventCount);
            await TryWriteFailureAsync(response, serializerOptions, cancellationToken);
        }
        finally
        {
            await events.DisposeAsync();
        }
    }

    internal static void WriteEvent(PipeWriter writer, ChatStreamEvent chatEvent, JsonSerializerOptions serializerOptions)
    {
        writer.Write("event: "u8);
        writer.Write(Encoding.UTF8.GetBytes(chatEvent.EventType));
        writer.Write("\ndata: "u8);
        using (var json = new Utf8JsonWriter(writer, WriterOptions))
        {
            JsonSerializer.Serialize(json, chatEvent, chatEvent.GetType(), serializerOptions);
        }

        writer.Write("\n\n"u8);
    }

    private async Task TryWriteFailureAsync(HttpResponse response, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            WriteEvent(response.BodyWriter, new ErrorEvent("The chat stream failed unexpectedly.", "internal_error"), serializerOptions);
            await response.BodyWriter.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or InvalidOperationException)
        {
            LogFailureEventNotWritten(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat stream closed by the client after {EventCount} event(s)")]
    private static partial void LogClientDisconnected(ILogger logger, int eventCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat stream failed after {EventCount} event(s)")]
    private static partial void LogStreamFailed(ILogger logger, Exception exception, int eventCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not write the failure event to the chat stream")]
    private static partial void LogFailureEventNotWritten(ILogger logger, Exception exception);
}
