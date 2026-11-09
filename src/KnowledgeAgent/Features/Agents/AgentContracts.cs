namespace KnowledgeAgent.Features.Agents;

public sealed record AgentDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    string? Instructions,
    string? Language,
    string ProfileName,
    IReadOnlyList<Guid> FolderIds,
    IReadOnlyList<string> Tools,
    bool AllowGeneralKnowledge,
    HandoverPolicy HandoverPolicy,
    bool IsBuiltIn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<AgentMcpAppDto> McpApps { get; init; } = [];

    public bool McpServerEnabled { get; init; }
    public bool HttpApiEnabled { get; init; }

    public IReadOnlyList<AgentSpecialistDto> Specialists { get; init; } = [];
    public bool ShowSpecialistName { get; init; }
}

public sealed record AgentSpecialistDto(Guid AgentId, string WhenToUse);

public sealed record AgentReferenceDto(Guid Id, string Name);

public sealed record AgentMcpAppDto(
    Guid ServerId,
    bool Enabled = true,
    McpAppMode Mode = McpAppMode.All,
    McpToolPolicy ChangesPolicy = McpToolPolicy.Confirm,
    IReadOnlyList<AgentMcpToolDto>? Tools = null);

// Fingerprint: the definition the admin approved. Sent back unchanged it keeps that approval; the tool's current
// fingerprint from the connection approves the definition the app lists now.
public sealed record AgentMcpToolDto(string Name, McpToolPolicy? Policy = null, string? Fingerprint = null);

public sealed record SaveAgentRequest(
    string Name,
    string? Key = null,
    string? Description = null,
    string? Instructions = null,
    string? Language = null,
    string? ProfileName = null,
    IReadOnlyList<Guid>? FolderIds = null,
    IReadOnlyList<string>? Tools = null,
    bool AllowGeneralKnowledge = false,
    HandoverPolicy? HandoverPolicy = null,
    IReadOnlyList<AgentMcpAppDto>? McpApps = null)
{
    public IReadOnlyList<AgentSpecialistDto>? Specialists { get; init; }
    public bool? ShowSpecialistName { get; init; }
}

public sealed record AgentOptionsDto(IReadOnlyList<string> Tools, IReadOnlyList<string> Profiles);

internal static class AgentMappings
{
    public static AgentDto ToDto(this AgentConfig agent) => new(
        agent.Id,
        agent.Key,
        agent.Name,
        agent.Description,
        agent.Instructions,
        agent.Language,
        agent.ProfileName,
        agent.FolderIds,
        agent.Tools,
        agent.AllowGeneralKnowledge,
        agent.HandoverPolicy,
        agent.IsBuiltIn,
        agent.CreatedAt,
        agent.UpdatedAt)
    {
        McpApps = agent.McpTools
            .Select(app => new AgentMcpAppDto(app.ServerId, app.Enabled, app.Mode, app.ChangesPolicy, app.Tools.Select(tool => new AgentMcpToolDto(tool.Name, tool.Policy, tool.Approved is null ? null : tool.Fingerprint)).ToList()))
            .ToList(),
        McpServerEnabled = agent.McpServerEnabled,
        HttpApiEnabled = agent.HttpApiEnabled,
        Specialists = agent.Specialists.Select(specialist => new AgentSpecialistDto(specialist.AgentId, specialist.WhenToUse)).ToList(),
        ShowSpecialistName = agent.ShowSpecialistName,
    };
}

public sealed class AgentIsSpecialistException(IReadOnlyList<AgentReferenceDto> agents)
    : Exception($"{string.Join(", ", agents.Select(agent => agent.Name))} hand conversations to this agent.")
{
    public IReadOnlyList<AgentReferenceDto> Agents { get; } = agents;
}
