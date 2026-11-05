using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Api.Tests.Support;

public static class RecordedRequests
{
    public static string SystemPrompt(this RecordedChatRequest request) =>
        request.Messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text is { Length: > 0 } system
            ? system
            : request.Options?.Instructions
              ?? throw new ShouldAssertException("The model request carries no system prompt.");

    public static IReadOnlyList<ChatMessage> ConversationMessages(this RecordedChatRequest request) =>
        request.Messages.Where(m => m.Role != ChatRole.System).ToList();

    public static IEnumerable<T> ContentsOf<T>(this IEnumerable<ChatMessage> messages) where T : AIContent =>
        messages.SelectMany(m => m.Contents).OfType<T>();

    // Not synchronized with the client, so call it only when no request is in flight.
    public static IReadOnlyList<RecordedChatRequest> Snapshot(this RuleBasedChatClient client) => client.Requests.ToList();
}
