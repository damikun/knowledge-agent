using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.History;

// A failed or stopped turn still goes into the history, so the next turn knows which tools already ran and does not
// offer a hand-over or call an MCP action a second time. It is closed the way providers expect: every call gets a
// result and the turn ends with a reply that says what happened.
internal static class InterruptedTurn
{
    internal const string StoppedNote = "(The person stopped this reply here.)";

    internal const string StoppedBeforeReply = "(The person stopped this turn before a reply was written.)";

    internal const string FailedReply = "(This turn failed before a reply was written. The tools that returned a result above did run.)";

    internal const string RanResult = "The turn ended before this result was read, but the tool ran. Do not run it again unless the person asks.";

    internal const string MayHaveRunResult = "The turn ended while this tool was running, so it may have run. Check before running it again.";

    public static string StoppedReply(string shown) =>
        string.IsNullOrWhiteSpace(shown) ? StoppedBeforeReply : $"{shown.TrimEnd()}\n\n{StoppedNote}";

    public static List<ChatMessage> Close(IReadOnlyList<ChatMessage> messages, IReadOnlySet<string> finishedCallIds, string reply)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var closed = messages.ToList();

        // The thinking was made for a turn that never finished, so it is not sent again.
        ConversationHistoryBuilder.DropReasoningOfCurrentTurn(closed);

        var answered = closed
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var unanswered = closed
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Where(call => !call.InformationalOnly && !answered.Contains(call.CallId))
            .Select(call => (AIContent)new FunctionResultContent(call.CallId, finishedCallIds.Contains(call.CallId) ? RanResult : MayHaveRunResult))
            .ToList();
        if (unanswered.Count > 0)
        {
            closed.Add(new ChatMessage(ChatRole.Tool, unanswered));
        }

        closed.Add(new ChatMessage(ChatRole.Assistant, reply));
        return closed;
    }
}
