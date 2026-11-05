using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat;

internal static class ChatJson
{
    public static JsonSerializerOptions Dto => JsonSerializerOptions.Web;

    // M.E.AI's own options, so reasoning and tool contents round-trip when the history is replayed.
    public static JsonSerializerOptions Messages => AIJsonUtilities.DefaultOptions;

    public static string SerializeMessages(IEnumerable<ChatMessage> messages, Action<string>? onDropped = null)
    {
        var array = messages.ToArray();
        try
        {
            return JsonSerializer.Serialize(array, Messages);
        }
        catch (NotSupportedException)
        {
            // Provider-specific content types could not be replayed anyway, so drop them instead of failing the turn.
            var sanitized = new List<ChatMessage>(array.Length);
            foreach (var message in array)
            {
                var kept = new List<AIContent>(message.Contents.Count);
                foreach (var content in message.Contents)
                {
                    if (CanSerialize(content))
                    {
                        kept.Add(content);
                    }
                    else
                    {
                        onDropped?.Invoke(content.GetType().FullName ?? content.GetType().Name);
                    }
                }

                if (kept.Count > 0)
                {
                    var copy = message.Clone();
                    copy.Contents = kept;
                    sanitized.Add(copy);
                }
            }

            return JsonSerializer.Serialize(sanitized.ToArray(), Messages);
        }
    }

    public static IReadOnlyList<ChatMessage> DeserializeMessages(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<ChatMessage[]>(json, Messages) ?? [];

    public static string SerializeDtos<T>(IReadOnlyList<T> items) => JsonSerializer.Serialize(items, Dto);

    public static IReadOnlyList<T> DeserializeDtos<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, Dto) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool CanSerialize(AIContent content)
    {
        try
        {
            _ = JsonSerializer.Serialize(content, Messages);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
