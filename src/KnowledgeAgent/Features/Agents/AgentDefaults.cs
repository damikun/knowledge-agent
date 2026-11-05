using KnowledgeAgent.Features.Chat.Tools;

namespace KnowledgeAgent.Features.Agents;

public static class AgentDefaults
{
    public const string ProfileName = "orchestrator";

    public static readonly IReadOnlyList<string> Tools =
    [
        ToolNames.SearchKnowledgeBase,
        ToolNames.GrepSources,
        ToolNames.FindSources,
        ToolNames.ReadSource,
        ToolNames.DelegateResearch,
        ToolNames.RecordFeedback,
    ];
}
