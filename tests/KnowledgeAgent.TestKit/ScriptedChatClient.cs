using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.TestKit;

public sealed record RecordedChatRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>> _steps = new();
    private readonly Lock _gate = new();
    private int _callCounter;

    public List<RecordedChatRequest> Requests { get; } = [];

    public string? FallbackText { get; set; }

    public ScriptedChatClient ThenText(string text, string? reasoning = null) => Then((_, _) =>
    {
        var contents = new List<AIContent>();
        if (reasoning is not null)
        {
            contents.Add(new TextReasoningContent(reasoning) { ProtectedData = "sig-" + Guid.NewGuid().ToString("N") });
        }

        contents.Add(new TextContent(text));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)) { FinishReason = ChatFinishReason.Stop };
    });

    public ScriptedChatClient ThenToolCall(string toolName, object? arguments = null) => ThenToolCalls((toolName, arguments));

    public ScriptedChatClient ThenToolCalls(params (string ToolName, object? Arguments)[] calls) => Then((_, _) =>
    {
        var contents = calls
            .Select(call => (AIContent)new FunctionCallContent(NextCallId(), call.ToolName, ToArguments(call.Arguments)))
            .ToList();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)) { FinishReason = ChatFinishReason.ToolCalls };
    });

    // A short note before the call, as models often write: "I'll check the returns policy."
    public ScriptedChatClient ThenTextAndToolCall(string text, string toolName, object? arguments = null) => Then((_, _) =>
        new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new TextContent(text), new FunctionCallContent(NextCallId(), toolName, ToArguments(arguments))]))
        {
            FinishReason = ChatFinishReason.ToolCalls,
        });

    public ScriptedChatClient ThenThrow(Exception exception) => Then((_, _) => throw exception);

    public ScriptedChatClient Then(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> step)
    {
        lock (_gate)
        {
            _steps.Enqueue(step);
        }

        return this;
    }

    public int RemainingSteps
    {
        get
        {
            lock (_gate)
            {
                return _steps.Count;
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = messages.ToList();
        Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>? step;
        lock (_gate)
        {
            Requests.Add(new RecordedChatRequest(snapshot, options?.Clone()));
            _steps.TryDequeue(out step);
        }

        ChatResponse response;
        if (step is not null)
        {
            response = step(snapshot, options);
        }
        else if (FallbackText is not null)
        {
            response = new ChatResponse(new ChatMessage(ChatRole.Assistant, FallbackText)) { FinishReason = ChatFinishReason.Stop };
        }
        else
        {
            throw new InvalidOperationException($"ScriptedChatClient script exhausted after {Requests.Count - 1} call(s).");
        }

        response.ModelId ??= options?.ModelId ?? "scripted-model";
        response.ResponseId ??= "resp-" + Guid.NewGuid().ToString("N");
        response.Usage ??= FakeUsage.Estimate(snapshot, response);
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in FakeStreaming.ToUpdates(response))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private string NextCallId() => $"call_{Interlocked.Increment(ref _callCounter)}_{Guid.NewGuid():N}"[..20];

    internal static Dictionary<string, object?> ToArguments(object? arguments) => arguments switch
    {
        null => new Dictionary<string, object?>(),
        IDictionary<string, object?> dictionary => new Dictionary<string, object?>(dictionary),
        _ => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments)) ?? new(),
    };
}

internal static class FakeUsage
{
    public static UsageDetails Estimate(IReadOnlyList<ChatMessage> input, ChatResponse response)
    {
        long inputTokens = input.Sum(m => (long)(m.Text?.Length ?? 0)) / 4 + 1;
        long outputTokens = response.Messages.Sum(m => (long)(m.Text?.Length ?? 0)) / 4 + 1;
        return new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens, TotalTokenCount = inputTokens + outputTokens };
    }
}

internal static class FakeStreaming
{
    public static IEnumerable<ChatResponseUpdate> ToUpdates(ChatResponse response)
    {
        var messageId = "msg-" + Guid.NewGuid().ToString("N");
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    foreach (var piece in Split(text.Text))
                    {
                        yield return Update(response, messageId, new TextContent(piece));
                    }
                }
                else
                {
                    yield return Update(response, messageId, content);
                }
            }
        }

        var final = new ChatResponseUpdate(ChatRole.Assistant, [])
        {
            MessageId = messageId,
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            FinishReason = response.FinishReason,
        };
        if (response.Usage is not null)
        {
            final.Contents.Add(new UsageContent(response.Usage));
        }

        yield return final;
    }

    private static ChatResponseUpdate Update(ChatResponse response, string messageId, AIContent content) =>
        new(ChatRole.Assistant, [content])
        {
            MessageId = messageId,
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
        };

    private static IEnumerable<string> Split(string text)
    {
        const int size = 12;
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }
}
