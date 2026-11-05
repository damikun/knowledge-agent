using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts.Rendering;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Prompts;

internal static class PromptTemplateExtensions
{
    // Cached templates are shared, and a JsonNode handed out twice could be mutated or re-parented by a caller.
    public static PromptTemplate Snapshot(this PromptTemplate template) =>
        template.Config is null ? template : template with { Config = (JsonObject)template.Config.DeepClone() };

    public static IReadOnlyList<string> ExtractVariables(this PromptTemplate template, IPromptTemplateRenderer renderer)
    {
        if (template.Type == PromptType.Text)
        {
            return renderer.ExtractVariables(template.Text ?? string.Empty);
        }

        return template.Messages
            .SelectMany(message => renderer.ExtractVariables(message.Content))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static ChatRole ToChatRole(this PromptMessage message) => message.Role.Trim().ToLowerInvariant() switch
    {
        "system" or "developer" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
