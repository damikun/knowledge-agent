namespace KnowledgeAgent.Features.Prompts;

public sealed class PromptNotFoundException(string name) : Exception($"Prompt '{name}' was not found.")
{
    public string Name { get; } = name;
}

public sealed class PromptRenderException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PromptStoreUnavailableException(string promptName, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string PromptName { get; } = promptName;
}
