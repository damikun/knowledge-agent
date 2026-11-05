using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Connections.Mcp;

public interface IMcpServerRepository
{
    Task<IReadOnlyList<McpServer>> ListAsync(CancellationToken cancellationToken = default);
    Task<McpServer?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<McpServer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default);
    Task AddAsync(McpServer server, CancellationToken cancellationToken = default);
    Task UpdateAsync(McpServer server, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class McpServerRepository(KnowledgeDbContext db, DbContextGate gate) : IMcpServerRepository
{
    public async Task<IReadOnlyList<McpServer>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var servers = await db.McpServers.AsNoTracking().ToListAsync(cancellationToken);
        return servers.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<McpServer?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.McpServers.AsNoTracking().FirstOrDefaultAsync(server => server.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<McpServer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.McpServers.AsNoTracking().Where(server => ids.Contains(server.Id)).ToListAsync(cancellationToken);
    }

    public async Task<bool> KeyExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.McpServers.AnyAsync(server => server.Key == key, cancellationToken);
    }

    public async Task AddAsync(McpServer server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.McpServers.Add(server);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateAsync(McpServer server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.McpServers.Update(server);
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
            var server = await db.McpServers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (server is null)
            {
                return false;
            }

            db.McpServers.Remove(server);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
