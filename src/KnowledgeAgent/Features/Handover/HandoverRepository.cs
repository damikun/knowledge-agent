using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Handover;

public interface IHandoverRepository
{
    Task<Handover?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Handover?> FindOpenAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<Handover?> FindLatestAsync(Guid conversationId, CancellationToken cancellationToken = default);
    Task<Handover?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<Handover?> FindBySlackThreadAsync(string channelId, string threadTs, CancellationToken cancellationToken = default);
    Task<Handover> AddAsync(Handover handover, CancellationToken cancellationToken = default);

    // Status and delivery fields are saved separately so jobs and people never undo each other's changes.
    Task SaveStatusAsync(Handover handover, CancellationToken cancellationToken = default);

    Task SaveDeliveryAsync(Handover handover, CancellationToken cancellationToken = default);

    Task<bool> TryEndPendingAsync(Handover handover, CancellationToken cancellationToken = default);

    Task<bool> TryMarkInboundAsync(string id, DateTimeOffset receivedAt, CancellationToken cancellationToken = default);

    Task<bool> SetAssigneeAsync(Guid conversationId, string? assigneeId, string? assigneeName, DateTimeOffset? assignedAt, CancellationToken cancellationToken = default);
}

internal sealed class HandoverRepository(KnowledgeDbContext db, DbContextGate gate) : IHandoverRepository
{
    private static readonly string[] StatusFields =
    [
        nameof(Handover.Status), nameof(Handover.Assignee), nameof(Handover.AssigneeId), nameof(Handover.AcceptedAt), nameof(Handover.ResolvedAt), nameof(Handover.UpdatedAt),
    ];

    private static readonly string[] DeliveryFields =
    [
        nameof(Handover.SlackChannelId), nameof(Handover.SlackThreadTs), nameof(Handover.EmailMessageId), nameof(Handover.EmailedAt),
        nameof(Handover.EscalatedAt), nameof(Handover.LastError), nameof(Handover.UpdatedAt),
    ];

    public async Task<Handover?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<Handover?> FindOpenAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking()
            .Where(item => item.ConversationId == conversationId && (item.Status == HandoverStatus.Pending || item.Status == HandoverStatus.Active))
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Handover?> FindLatestAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking()
            .Where(item => item.ConversationId == conversationId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Handover?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking().FirstOrDefaultAsync(item => item.Token == token, cancellationToken);
    }

    public async Task<Handover?> FindBySlackThreadAsync(string channelId, string threadTs, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SlackChannelId == channelId && item.SlackThreadTs == threadTs, cancellationToken);
    }

    public async Task<Handover> AddAsync(Handover handover, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handover);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Handovers.Add(handover);
            await db.SaveChangesAsync(cancellationToken);
            return handover;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public Task SaveStatusAsync(Handover handover, CancellationToken cancellationToken = default) =>
        SaveAsync(handover, StatusFields, cancellationToken);

    public Task SaveDeliveryAsync(Handover handover, CancellationToken cancellationToken = default) =>
        SaveAsync(handover, DeliveryFields, cancellationToken);

    public async Task<bool> TryEndPendingAsync(Handover handover, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handover);
        using var _ = await gate.EnterAsync(cancellationToken);
        var pending = db.Handovers.Where(item => item.Id == handover.Id && item.Status == HandoverStatus.Pending);
        if (db.Database.IsRelational())
        {
            // One statement, so a person joining at the same moment either wins or finds the request ended.
            return await pending.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, handover.Status)
                    .SetProperty(item => item.ResolvedAt, handover.ResolvedAt)
                    .SetProperty(item => item.UpdatedAt, handover.UpdatedAt),
                cancellationToken) == 1;
        }

        // Non-relational providers have no ExecuteUpdate.
        try
        {
            if (await pending.FirstOrDefaultAsync(cancellationToken) is not { } stored)
            {
                return false;
            }

            stored.Status = handover.Status;
            stored.ResolvedAt = handover.ResolvedAt;
            stored.UpdatedAt = handover.UpdatedAt;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> TryMarkInboundAsync(string id, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!db.Database.IsRelational() && await db.HandoverInbound.AnyAsync(item => item.Id == id, cancellationToken))
            {
                return false;
            }

            db.HandoverInbound.Add(new HandoverInbound { Id = id, ReceivedAt = receivedAt });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> SetAssigneeAsync(
        Guid conversationId,
        string? assigneeId,
        string? assigneeName,
        DateTimeOffset? assignedAt,
        CancellationToken cancellationToken = default)
    {
        var id = assigneeName is null ? null : assigneeId;
        var at = assigneeName is null ? null : assignedAt;
        using var _ = await gate.EnterAsync(cancellationToken);
        var conversation = db.Conversations.Where(item => item.Id == conversationId);
        if (db.Database.IsRelational())
        {
            // Only the assignee: the list orders by activity, and an assignment is not activity.
            return await conversation.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.AssigneeId, id)
                    .SetProperty(item => item.AssigneeName, assigneeName)
                    .SetProperty(item => item.AssignedAt, at),
                cancellationToken) == 1;
        }

        try
        {
            if (await conversation.FirstOrDefaultAsync(cancellationToken) is not { } stored)
            {
                return false;
            }

            stored.AssigneeId = id;
            stored.AssigneeName = assigneeName;
            stored.AssignedAt = at;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task SaveAsync(Handover handover, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handover);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var entry = db.Handovers.Attach(handover);
            foreach (var field in fields)
            {
                entry.Property(field).IsModified = true;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
