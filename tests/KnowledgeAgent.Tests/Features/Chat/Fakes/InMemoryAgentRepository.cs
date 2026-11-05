using System.Collections.Concurrent;
using KnowledgeAgent.Features.Agents;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

internal sealed class InMemoryAgentRepository : IAgentRepository
{
    private readonly ConcurrentDictionary<Guid, AgentConfig> _agents = new();

    public int Count => _agents.Count;

    public AgentConfig Add(AgentConfig agent)
    {
        if (agent.Id == Guid.Empty)
        {
            agent.Id = Guid.CreateVersion7();
        }

        _agents[agent.Id] = Clone(agent);
        return agent;
    }

    public Task<IReadOnlyList<AgentConfig>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentConfig>>(_agents.Values.Select(Clone).OrderByDescending(a => a.IsBuiltIn).ThenBy(a => a.Name).ToList());

    public Task<AgentConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_agents.TryGetValue(id, out var agent) ? Clone(agent) : null);

    public Task<AgentConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_agents.Values.Where(agent => agent.Key == key).Select(Clone).FirstOrDefault());

    public Task<AgentConfig> AddAsync(AgentConfig agent, CancellationToken cancellationToken = default) => Task.FromResult(Add(agent));

    public Task UpdateAsync(AgentConfig agent, CancellationToken cancellationToken = default)
    {
        _agents[agent.Id] = Clone(agent);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_agents.TryRemove(id, out _));

    private static AgentConfig Clone(AgentConfig agent) => new()
    {
        Id = agent.Id,
        Key = agent.Key,
        Name = agent.Name,
        Description = agent.Description,
        Instructions = agent.Instructions,
        Language = agent.Language,
        ProfileName = agent.ProfileName,
        FolderIds = [.. agent.FolderIds],
        Tools = [.. agent.Tools],
        AllowGeneralKnowledge = agent.AllowGeneralKnowledge,
        HandoverPolicy = agent.HandoverPolicy,
        IsBuiltIn = agent.IsBuiltIn,
        McpTools = agent.McpTools
            .Select(app => new AgentMcpApp
            {
                ServerId = app.ServerId,
                Enabled = app.Enabled,
                Mode = app.Mode,
                ChangesPolicy = app.ChangesPolicy,
                Tools = app.Tools.Select(tool => new AgentMcpTool { Name = tool.Name, Policy = tool.Policy }).ToList(),
            })
            .ToList(),
        McpServerEnabled = agent.McpServerEnabled,
        HttpApiEnabled = agent.HttpApiEnabled,
        Specialists = agent.Specialists.Select(specialist => new AgentSpecialist { AgentId = specialist.AgentId, WhenToUse = specialist.WhenToUse }).ToList(),
        ShowSpecialistName = agent.ShowSpecialistName,
        CreatedAt = agent.CreatedAt,
        UpdatedAt = agent.UpdatedAt,
    };
}
