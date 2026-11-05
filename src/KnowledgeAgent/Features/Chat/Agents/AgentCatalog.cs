using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class AgentCatalog
{
    public const string OrchestratorName = "orchestrator";
    public const string ResearcherName = "researcher";

    private const string ResearchToolDescription =
        "Delegates a self-contained research task to the researcher sub-agent. The researcher runs several searches "
        + "and reads sources on its own, then returns a concise report whose facts carry [S#] citation tags you can "
        + "reuse in your answer. Use it for broad, multi-part or comparative questions; answer simple lookups yourself "
        + "with search_knowledge_base. Describe the task completely: the researcher does not see the conversation.";

    public AgentCatalog(IOptions<AgentOptions> options)
    {
        var settings = options.Value;

        Researcher = new AgentDefinition(
            ResearcherName,
            PromptNames.ResearcherSystem,
            settings.ResearcherProfile,
            [ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.ReadSource],
            Math.Max(1, settings.ResearcherMaxIterations))
        {
            Delegation = new SubAgentToolSpec(ToolNames.DelegateResearch, ResearchToolDescription),
        };

        SubAgents = settings.ResearcherEnabled ? [Researcher] : [];

        Orchestrator = new AgentDefinition(
            OrchestratorName,
            PromptNames.OrchestratorSystem,
            settings.OrchestratorProfile,
            [
                ToolNames.SearchKnowledgeBase,
                ToolNames.GrepSources,
                ToolNames.FindSources,
                ToolNames.ReadSource,
                .. SubAgents.Select(agent => agent.Delegation!.ToolName),
                ToolNames.RecordFeedback,
                ToolNames.RequestHumanHandover,
            ],
            Math.Max(1, settings.MaxIterations));
    }

    public AgentDefinition Orchestrator { get; }

    public AgentDefinition Researcher { get; }

    public IReadOnlyList<AgentDefinition> SubAgents { get; }
}
