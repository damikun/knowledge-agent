using KnowledgeAgent.Infrastructure.Langfuse;

namespace KnowledgeAgent.Features.Prompts;

internal static class PromptStoreMode
{
    public const string Langfuse = "Langfuse";
    public const string Local = "Local";

    public static bool IsLocal(PromptOptions options) => string.Equals(options.Store?.Trim(), Local, StringComparison.OrdinalIgnoreCase);

    public static bool UsesLangfuse(PromptOptions prompts, LangfuseOptions langfuse) => !IsLocal(prompts) && langfuse.IsConfigured;
}
