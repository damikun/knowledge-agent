using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Infrastructure.AI.Profiles;

namespace KnowledgeAgent.Features.Prompts;

public static class PromptConfig
{
    public static ModelProfilePatch? ParseModelPatch(JsonObject? config)
    {
        if (config is null)
        {
            return null;
        }

        var patch = new ModelProfilePatch(
            Provider: GetString(config, "provider"),
            Model: GetString(config, "model"),
            Temperature: GetNumber(config, "temperature") is { } temperature ? (float)temperature : null,
            MaxOutputTokens: GetNumber(config, "maxOutputTokens", "max_output_tokens", "maxTokens", "max_tokens") is { } max ? (int)max : null,
            ReasoningEffort: GetString(config, "reasoningEffort", "reasoning_effort"),
            ReasoningOutput: GetString(config, "reasoningOutput", "reasoning_output"));

        return patch.IsEmpty ? null : patch;
    }

    public static string? ParseProfile(JsonObject? config) => config is null ? null : GetString(config, "profile");

    private static string? GetString(JsonObject config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config[key] is JsonValue value
                && value.GetValueKind() == JsonValueKind.String
                && value.GetValue<string>() is { Length: > 0 } text)
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static double? GetNumber(JsonObject config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config[key] is not JsonValue value)
            {
                continue;
            }

            switch (value.GetValueKind())
            {
                case JsonValueKind.Number:
                    return value.GetValue<double>();
                case JsonValueKind.String when double.TryParse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
            }
        }

        return null;
    }
}
