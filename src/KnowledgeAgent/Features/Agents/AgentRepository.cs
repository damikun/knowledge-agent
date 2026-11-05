using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Agents;

public interface IAgentRepository
{
    Task<IReadOnlyList<AgentConfig>> ListAsync(CancellationToken cancellationToken = default);
    Task<AgentConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgentConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<AgentConfig> AddAsync(AgentConfig agent, CancellationToken cancellationToken = default);
    Task UpdateAsync(AgentConfig agent, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class AgentRepository(KnowledgeDbContext db, DbContextGate gate) : IAgentRepository
{
    public async Task<IReadOnlyList<AgentConfig>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var agents = await db.Agents.AsNoTracking().ToListAsync(cancellationToken);

        return agents
            .Select(agent => agent.ApplyBuiltInLimits())
            .OrderByDescending(agent => agent.IsBuiltIn)
            .ThenBy(agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AgentConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return (await db.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == id, cancellationToken))?.ApplyBuiltInLimits();
    }

    public async Task<AgentConfig?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return (await db.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Key == key, cancellationToken))?.ApplyBuiltInLimits();
    }

    public async Task<AgentConfig> AddAsync(AgentConfig agent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        agent.ApplyBuiltInLimits();
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Agents.Add(agent);
            await db.SaveChangesAsync(cancellationToken);
            return agent;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateAsync(AgentConfig agent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        agent.ApplyBuiltInLimits();
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Agents.Update(agent);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var agent = await db.Agents.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (agent is null)
            {
                return false;
            }

            db.Agents.Remove(agent);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
