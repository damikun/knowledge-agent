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

// Approved: the tool's definition matches what an admin approved. Changed: it differs since. NotApproved: never approved.
public enum McpToolApproval
{
    Approved,
    Changed,
    NotApproved,
}

public sealed record ResolvedMcpTool(McpToolInfo Tool, McpToolPolicy Policy, McpToolApproval Approval);

// Stored as JSON in agents.mcp_tools. The defaults below also read the older per-tool allowlist ({ serverId, tools }).
public sealed class AgentMcpApp
{
    public const int MaxToolsPerServer = 100;

    public Guid ServerId { get; set; }

    public bool Enabled { get; set; } = true;

    public McpAppMode Mode { get; set; } = McpAppMode.Chosen;

    public McpToolPolicy ChangesPolicy { get; set; } = McpToolPolicy.Confirm;

    // The picked tools, and in All mode the approved ones.
    public List<AgentMcpTool> Tools { get; set; } = [];

    // Annotations come from the server and may change, so only an admin's approval of the exact definition lets a tool
    // run on its own. Until a change is approved again, the model gets the approved description and the tool needs Confirm.
    public (List<ResolvedMcpTool> Tools, List<string> Missing) Resolve(IReadOnlyList<McpToolInfo> offered)
    {
        if (Mode == McpAppMode.All)
        {
            return (offered.Select(ResolveAll).ToList(), []);
        }

        var result = new List<ResolvedMcpTool>();
        var missing = new List<string>();
        foreach (var choice in Tools)
        {
            if (offered.FirstOrDefault(tool => tool.Name == choice.Name) is { } info)
            {
                result.Add(choice.Check(info) switch
                {
                    McpToolApproval.Approved => new ResolvedMcpTool(info, choice.Policy, McpToolApproval.Approved),
                    McpToolApproval.Changed => new ResolvedMcpTool(choice.Approved!, AtLeastConfirm(choice.Policy), McpToolApproval.Changed),
                    _ => new ResolvedMcpTool(info, AtLeastConfirm(choice.Policy), McpToolApproval.NotApproved),
                });
            }
            else
            {
                missing.Add(choice.Name);
            }
        }

        return (result, missing);
    }

    private ResolvedMcpTool ResolveAll(McpToolInfo info)
    {
        var approval = Tools.FirstOrDefault(tool => tool.Name == info.Name);
        return approval?.Check(info) switch
        {
            McpToolApproval.Approved => new ResolvedMcpTool(info, PolicyFor(info), McpToolApproval.Approved),
            McpToolApproval.Changed => new ResolvedMcpTool(approval.Approved!, AtLeastConfirm(ChangesPolicy), McpToolApproval.Changed),
            _ => new ResolvedMcpTool(info, AtLeastConfirm(ChangesPolicy), McpToolApproval.NotApproved),
        };
    }

    // A suggestion from the server's annotations; it only applies once an admin approves the tool.
    public McpToolPolicy PolicyFor(McpToolInfo tool) => tool.MayChangeThings ? ChangesPolicy : McpToolPolicy.Auto;

    private static McpToolPolicy AtLeastConfirm(McpToolPolicy policy) => policy == McpToolPolicy.Auto ? McpToolPolicy.Confirm : policy;
}

public sealed class AgentMcpTool
{
    public string Name { get; set; } = string.Empty;

    public McpToolPolicy Policy { get; set; } = McpToolPolicy.Confirm;

    // SHA-256 of the approved definition; null for tools saved before approvals existed.
    public string? Fingerprint { get; set; }

    // The definition as approved, offered to the model while the server's current one waits for approval.
    public McpToolInfo? Approved { get; set; }

    public static AgentMcpTool Approve(McpToolInfo tool, McpToolPolicy policy) =>
        new() { Name = tool.Name, Policy = policy, Fingerprint = McpToolFingerprint.Compute(tool), Approved = tool };

    public McpToolApproval Check(McpToolInfo current) =>
        Approved is null || Fingerprint is null ? McpToolApproval.NotApproved
        : McpToolFingerprint.Matches(current, Fingerprint) ? McpToolApproval.Approved
        : McpToolApproval.Changed;
}
