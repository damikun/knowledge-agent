using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Chat;

// Must sit inside the OpenTelemetry stage so that Activity.Current is the GenAI span when the prompt tags are set.
internal sealed class PromptTelemetryChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public const string PromptNameKey = KnowledgeAgentTelemetry.LangfusePromptNameKey;
    public const string PromptVersionKey = KnowledgeAgentTelemetry.LangfusePromptVersionKey;

    public const string PromptNameTag = "langfuse.observation.prompt.name";
    public const string PromptVersionTag = "langfuse.observation.prompt.version";

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Apply(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);

    internal static ChatOptions? Apply(ChatOptions? options)
    {
        if (options?.AdditionalProperties is not { Count: > 0 } properties
            || (!properties.ContainsKey(PromptNameKey) && !properties.ContainsKey(PromptVersionKey)))
        {
            return options;
        }

        properties.TryGetValue(PromptNameKey, out var name);
        properties.TryGetValue(PromptVersionKey, out var version);

        // Without a listener no GenAI span exists and Activity.Current is an outer span that must not be tagged.
        if (Activity.Current is { } activity && activity.Source.Name == KnowledgeAgentTelemetry.AI)
        {
            if (ToText(name) is { } promptName)
            {
                activity.SetTag(PromptNameTag, promptName);
            }

            if (ToVersion(version) is { } promptVersion)
            {
                activity.SetTag(PromptVersionTag, promptVersion);
            }
        }

        var remaining = new AdditionalPropertiesDictionary(properties);
        remaining.Remove(PromptNameKey);
        remaining.Remove(PromptVersionKey);

        var clone = options.Clone();
        clone.AdditionalProperties = remaining.Count == 0 ? null : remaining;
        return clone;
    }

    private static string? ToText(object? value) => value switch
    {
        null => null,
        string text => string.IsNullOrWhiteSpace(text) ? null : text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    // Langfuse expects an integer version, so anything non-numeric is passed through as text.
    private static object? ToVersion(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int or long:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            case JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var number):
                return number;
        }

        var text = ToText(value);
        if (text is null)
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : text;
    }
}
