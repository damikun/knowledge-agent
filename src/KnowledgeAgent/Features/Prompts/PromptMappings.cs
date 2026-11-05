using KnowledgeAgent.Features.Models;

namespace KnowledgeAgent.Features.Prompts;

internal static class PromptMappings
{
    public static PromptSummaryDto ToDto(this PromptSummary summary) => new(
        summary.Name,
        summary.Origin.ToContractValue(),
        summary.Version,
        summary.Type.ToContractValue(),
        summary.Labels,
        summary.Variables,
        summary.ExternalUrl);

    public static PromptDetailDto ToDetailDto(this PromptTemplate template, IReadOnlyList<string> variables, string? externalUrl) => new(
        template.Name,
        template.Origin.ToContractValue(),
        template.Version,
        template.Type.ToContractValue(),
        template.Text,
        template.Messages.Select(m => new PromptMessageDto(m.Role, m.Content)).ToArray(),
        template.Config,
        template.Labels,
        variables,
        externalUrl);

    public static RenderedPromptDto ToDto(this RenderedPrompt prompt) => new(
        prompt.Name,
        prompt.Origin.ToContractValue(),
        prompt.Version,
        prompt.Type.ToContractValue(),
        prompt.Text,
        prompt.Messages.Select(m => new PromptMessageDto(m.Role, m.Content)).ToArray(),
        prompt.Config,
        prompt.Profile,
        prompt.ModelPatch?.ToSettingsDto());

    public static string ToContractValue(this PromptOrigin origin) => origin == PromptOrigin.Local ? "local" : "langfuse";

    public static string ToContractValue(this PromptType type) => type == PromptType.Chat ? "chat" : "text";
}
