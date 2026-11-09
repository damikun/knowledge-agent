using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat.Support;

internal static class EventExtensions
{
    public static IReadOnlyList<string> CollapsedTypes(this IEnumerable<ChatStreamEvent> events)
    {
        var types = new List<string>();
        foreach (var streamEvent in events)
        {
            if (streamEvent.EventType == ChatEventTypes.TextDelta && types.Count > 0 && types[^1] == ChatEventTypes.TextDelta)
            {
                continue;
            }

            types.Add(streamEvent.EventType);
        }

        return types;
    }

    public static string Text(this IEnumerable<ChatStreamEvent> events, string agent = "orchestrator") =>
        string.Concat(events.OfType<TextDeltaEvent>().Where(e => e.Agent == agent).Select(e => e.Text));

    public static Guid ConversationId(this IEnumerable<ChatStreamEvent> events) => events.OfType<ConversationEvent>().First().ConversationId;

    public static DoneEvent Done(this IEnumerable<ChatStreamEvent> events) => events.OfType<DoneEvent>().ShouldHaveSingleItem();
}

internal static class TurnExtensions
{
    public static IReadOnlyList<ChatMessage> ModelMessages(this ConversationTurn turn) => ChatJson.DeserializeMessages(turn.ModelMessagesJson);

    public static IReadOnlyList<ToolCallDto> ToolCalls(this ConversationTurn turn) => ChatJson.DeserializeDtos<ToolCallDto>(turn.ToolCallsJson);

    public static IReadOnlyList<CitationDto> Citations(this ConversationTurn turn) => ChatJson.DeserializeDtos<CitationDto>(turn.CitationsJson);
}

internal static class MessageExtensions
{
    public static string ToJson(this ChatMessage message) => JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions);

    public static IReadOnlyList<string> ToJson(this IEnumerable<ChatMessage> messages) => messages.Select(ToJson).ToList();

    public static IReadOnlyList<FunctionResultContent> ToolResults(this ChatMessage message) =>
        message.Contents.OfType<FunctionResultContent>().ToList();

    public static string ResultText(this FunctionResultContent result) => result.Result?.ToString() ?? string.Empty;

    // The tool's own text inside the untrusted-content wrapper.
    public static string Unwrapped(this string text)
    {
        if (!text.StartsWith(UntrustedText.Notice, StringComparison.Ordinal))
        {
            return text;
        }

        var start = text.IndexOf('\n', UntrustedText.Notice.Length + 1) + 1;
        var end = text.LastIndexOf("\n</" + UntrustedText.TagPrefix, StringComparison.Ordinal);
        return text[start..end];
    }
}

internal sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];
    private readonly Lock _gate = new();

    public ActivityCollector(params string[] sourceNames)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Stopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped.ToList();
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}

internal sealed class DelegateChatClient(
    Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        lock (Requests)
        {
            Requests.Add(snapshot);
        }

        return stream(snapshot, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static async IAsyncEnumerable<ChatResponseUpdate> TextThenWait(
        string text,
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = "m1" };
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "never") { MessageId = "m1" };
    }
}
