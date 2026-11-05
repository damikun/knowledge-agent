namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed record AgentDefinition(
    string Name,
    string PromptName,
    string ProfileName,
    IReadOnlyList<string> ToolNames,
    int MaxIterations)
{
    public SubAgentToolSpec? Delegation { get; init; }

    public IReadOnlyList<Tools.IAgentTool> ExtraTools { get; init; } = [];

    public IReadOnlyList<Tools.IAgentTool> HandoffTools { get; init; } = [];
}

internal sealed record SubAgentToolSpec(string ToolName, string Description);
