using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.TestKit;

public sealed partial class RuleBasedChatClient : IChatClient
{
    public const string NoResultsAnswer = "I could not find information about that in the knowledge base.";

    private readonly Lock _gate = new();

    public List<RecordedChatRequest> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        lock (_gate)
        {
            Requests.Add(new RecordedChatRequest(list, options?.Clone()));
        }

        var response = Decide(list, options);
        response.ModelId ??= options?.ModelId ?? "rule-based";
        response.ResponseId ??= "resp-" + Guid.NewGuid().ToString("N");
        response.Usage ??= FakeUsage.Estimate(list, response);
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
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static ChatResponse Decide(List<ChatMessage> messages, ChatOptions? options)
    {
        var lastUserIndex = messages.FindLastIndex(m => m.Role == ChatRole.User);
        var lastUserText = lastUserIndex >= 0 ? messages[lastUserIndex].Text : string.Empty;
        var toolResults = messages
            .Skip(lastUserIndex + 1)
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();

        var offersSearch = options?.Tools?.Any(t => t.Name == ToolNames.SearchKnowledgeBase) == true
            && options.ToolMode is not NoneChatToolMode;

        if (offersSearch && toolResults.Count == 0)
        {
            var call = new FunctionCallContent(
                "call_" + Guid.NewGuid().ToString("N")[..12],
                ToolNames.SearchKnowledgeBase,
                new Dictionary<string, object?> { ["query"] = lastUserText });
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls };
        }

        if (toolResults.Count > 0)
        {
            var resultText = string.Join("\n", toolResults.Select(r => r.Result?.ToString() ?? string.Empty));
            var tag = CitationTag().Match(resultText);
            if (tag.Success)
            {
                var after = resultText[(tag.Index + tag.Length)..];
                var snippet = after
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Skip(1)
                    .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
                if (snippet.Length > 160)
                {
                    snippet = snippet[..160];
                }

                return Text($"According to the knowledge base, {snippet} {tag.Value}");
            }

            return Text(NoResultsAnswer);
        }

        var words = lastUserText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5);
        return Text(string.Join(' ', words));
    }

    private static ChatResponse Text(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop };

    [GeneratedRegex(@"\[S\d+\]")]
    private static partial Regex CitationTag();
}
