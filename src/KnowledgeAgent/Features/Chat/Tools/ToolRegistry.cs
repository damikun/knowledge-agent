using KnowledgeAgent.Features.Chat.Agents;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    public ToolRegistry(IEnumerable<IAgentTool> tools, AgentCatalog catalog, IOptions<AgentOptions> options, TimeProvider timeProvider)
    {
        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
        }

        foreach (var agent in catalog.SubAgents)
        {
            if (agent.Delegation is { } delegation)
            {
                _tools[delegation.ToolName] = new SubAgentTool(agent, delegation, options.Value, timeProvider);
            }
        }
    }

    public IReadOnlyCollection<string> Names => _tools.Keys;

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);

    public IReadOnlyList<IAgentTool> Resolve(AgentDefinition agent) =>
        agent.ToolNames
            .Select(name => _tools.TryGetValue(name, out var tool)
                ? tool
                : throw new InvalidOperationException($"Agent '{agent.Name}' references the unknown tool '{name}'."))
            .Concat(agent.ExtraTools.Where(tool => !_tools.ContainsKey(tool.Name)))
            .Concat(agent.HandoffTools)
            .ToList();
}
