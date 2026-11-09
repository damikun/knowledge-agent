using System.Text.Json;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.History;

internal sealed record ConversationHistory(List<ChatMessage> Messages, HistoryMode Mode, int Turns, int StubbedResults, long EstimatedTokens);

// Stateless: the result depends only on the stored turns, so the prompt prefix stays the same from one turn to the next.
internal sealed class ConversationHistoryBuilder(IOptions<HistoryOptions> options, ILogger<ConversationHistoryBuilder> logger)
{
    public ConversationHistory Build(Conversation conversation)
    {
        var settings = options.Value;
        var history = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(conversation.SystemPrompt))
        {
            history.Add(new ChatMessage(ChatRole.System, conversation.SystemPrompt));
        }

        var turns = conversation.Turns.OrderBy(turn => turn.Sequence).ToList();
        var keepFullFrom = turns.Count - Math.Max(0, settings.KeepFullTurns);
        var stubbed = 0;
        for (var i = 0; i < turns.Count; i++)
        {
            IReadOnlyList<ChatMessage> messages;
            try
            {
                messages = ChatJson.DeserializeMessages(turns[i].ModelMessagesJson);
            }
            catch (JsonException ex)
            {
                AgentLog.HistoryTurnSkipped(logger, ex, turns[i].Id, conversation.Id);
                continue;
            }

            if (settings.Mode == HistoryMode.Verbatim)
            {
                history.AddRange(messages);
            }
            else
            {
                history.AddRange(CompactTurn(turns[i], messages, stubResults: i < keepFullFrom, settings, ref stubbed));
            }
        }

        return new ConversationHistory(history, settings.Mode, turns.Count, stubbed, EstimateTokens(history));
    }

    // Claude accepts a history whose earlier thinking blocks were all removed, but not one whose tool results changed
    // under a thinking block that is still there. So a finished turn always loses its thinking first.
    internal static List<ChatMessage> CompactTurn(
        ConversationTurn turn,
        IReadOnlyList<ChatMessage> messages,
        bool stubResults,
        HistoryOptions settings,
        ref int stubbed)
    {
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            toolNames.TryAdd(call.CallId, call.Name);
        }

        var stubs = stubResults ? new ToolResultStubs(turn, settings) : null;
        var compacted = new List<ChatMessage>(messages.Count);
        var questionSeen = false;
        foreach (var message in messages)
        {
            var isQuestion = message.Role == ChatRole.User && !questionSeen;
            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent:
                        break;
                    case FunctionResultContent result when stubs is not null:
                        var text = ToolResultStubs.TextOf(result.Result);
                        var stub = stubs.For(toolNames.GetValueOrDefault(result.CallId), text);
                        if (stub == text)
                        {
                            contents.Add(result);
                        }
                        else
                        {
                            contents.Add(new FunctionResultContent(result.CallId, stub));
                            stubbed++;
                        }

                        break;
                    case TextContent question when isQuestion && stubResults:
                        contents.Add(ShortenAppendedResult(question, turn.UserMessage, settings.McpExcerptChars));
                        break;
                    default:
                        contents.Add(content);
                        break;
                }
            }

            questionSeen |= message.Role == ChatRole.User;
            if (contents.Count > 0)
            {
                var copy = message.Clone();
                copy.Contents = contents;
                compacted.Add(copy);
            }
        }

        return compacted;
    }

    // Removes the thinking of the turn in progress before another agent takes over: its system prompt and tools
    // differ, so the blocks would no longer match what they were made for.
    internal static int DropReasoningOfCurrentTurn(List<ChatMessage> history)
    {
        var start = history.FindLastIndex(message => message.Role == ChatRole.User);
        var dropped = 0;
        for (var i = history.Count - 1; i >= Math.Max(0, start); i--)
        {
            var message = history[i];
            var kept = message.Contents.Where(content => content is not TextReasoningContent).ToList();
            if (kept.Count == message.Contents.Count)
            {
                continue;
            }

            dropped += message.Contents.Count - kept.Count;
            if (kept.Count == 0)
            {
                history.RemoveAt(i);
                continue;
            }

            var copy = message.Clone();
            copy.Contents = kept;
            history[i] = copy;
        }

        return dropped;
    }

    // A confirmed action's output is appended to the person's message; in later turns only its start is needed.
    private static TextContent ShortenAppendedResult(TextContent content, string question, int maxChars)
    {
        var text = content.Text;
        if (question.Length == 0 || !text.StartsWith(question, StringComparison.Ordinal))
        {
            return content;
        }

        var appended = text[question.Length..];
        var noticeAt = appended.IndexOf(UntrustedText.Notice, StringComparison.Ordinal);
        if (noticeAt >= 0 && ToolResultStubs.TryUnwrap(appended[noticeAt..], out var inner, out var key))
        {
            if (inner.Length <= Math.Max(0, maxChars))
            {
                return content;
            }

            var closing = "\n</" + UntrustedText.TagPrefix + key + ">";
            var blockEnd = appended.LastIndexOf(closing, StringComparison.Ordinal) + closing.Length;
            var block = UntrustedText.Wrap(TextUtil.Clip(inner, Math.Max(0, maxChars)) + "\n(Shortened in later turns.)", key);
            return new TextContent(question + appended[..noticeAt] + block + appended[blockEnd..]);
        }

        var limit = Math.Max(0, maxChars) + ToolResultStubs.KeepWholeChars;
        return appended.Length <= limit
            ? content
            : new TextContent(question + TextUtil.Clip(appended, limit) + "\n(Shortened in later turns.)");
    }

    internal static long EstimateTokens(IEnumerable<ChatMessage> messages)
    {
        long chars = 0;
        foreach (var content in messages.SelectMany(message => message.Contents))
        {
            chars += content switch
            {
                TextContent text => text.Text.Length,
                TextReasoningContent reasoning => (reasoning.Text?.Length ?? 0) + (reasoning.ProtectedData?.Length ?? 0),
                FunctionCallContent call => call.Name.Length + JsonSerializer.Serialize(call.Arguments, ChatJson.Messages).Length,
                FunctionResultContent result => ToolResultStubs.TextOf(result.Result).Length,
                _ => 0,
            };
        }

        return chars / 4;
    }
}
