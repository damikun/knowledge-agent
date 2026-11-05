using KnowledgeAgent.Features.Connections.Mcp;

namespace KnowledgeAgent.Features.Agents;

public enum McpToolPolicy
{
    Auto,

    Confirm,

    VerifiedOnly,
}

public enum McpAppMode
{
    All,
    Chosen,
}

// Stored as JSON in agents.mcp_tools. The defaults below also read the older per-tool allowlist ({ serverId, tools }).
public sealed class AgentMcpApp
{
    public const int MaxToolsPerServer = 100;

    public Guid ServerId { get; set; }

    public bool Enabled { get; set; } = true;

    public McpAppMode Mode { get; set; } = McpAppMode.Chosen;

    public McpToolPolicy ChangesPolicy { get; set; } = McpToolPolicy.Confirm;

    public List<AgentMcpTool> Tools { get; set; } = [];

    public (List<(McpToolInfo Tool, McpToolPolicy Policy)> Tools, List<string> Missing) Resolve(IReadOnlyList<McpToolInfo> offered)
    {
        if (Mode == McpAppMode.All)
        {
            return (offered.Select(tool => (tool, PolicyFor(tool))).ToList(), []);
        }

        var result = new List<(McpToolInfo, McpToolPolicy)>();
        var missing = new List<string>();
        foreach (var choice in Tools)
        {
            if (offered.FirstOrDefault(tool => tool.Name == choice.Name) is { } info)
            {
                result.Add((info, choice.Policy));
            }
            else
            {
                missing.Add(choice.Name);
            }
        }

        return (result, missing);
    }

    public McpToolPolicy PolicyFor(McpToolInfo tool) => tool.MayChangeThings ? ChangesPolicy : McpToolPolicy.Auto;
}

public sealed class AgentMcpTool
{
    public string Name { get; set; } = string.Empty;

    public McpToolPolicy Policy { get; set; } = McpToolPolicy.Confirm;
}
