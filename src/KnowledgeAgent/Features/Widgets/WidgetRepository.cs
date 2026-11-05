using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Widgets;

public interface IWidgetRepository
{
    Task<IReadOnlyList<Widget>> ListAsync(CancellationToken cancellationToken = default);
    Task<Widget?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Widget?> GetByKeyAsync(string publicKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Widget>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<Widget> AddAsync(Widget widget, CancellationToken cancellationToken = default);
    Task UpdateAsync(Widget widget, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WidgetAsset> AddAssetAsync(WidgetAsset asset, CancellationToken cancellationToken = default);
    Task<WidgetAsset?> GetAssetAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteUnusedAssetsAsync(Guid widgetId, IReadOnlyCollection<Guid> keep, CancellationToken cancellationToken = default);

    Task<WidgetVisitor?> GetVisitorAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WidgetVisitor> AddVisitorAsync(WidgetVisitor visitor, CancellationToken cancellationToken = default);
    Task UpdateVisitorAsync(WidgetVisitor visitor, CancellationToken cancellationToken = default);
    Task<int> DeleteVisitorsSeenBeforeAsync(Guid widgetId, DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<int> AddDailyMessageAsync(Guid widgetId, DateOnly day, CancellationToken cancellationToken = default);
}

internal sealed class WidgetRepository(KnowledgeDbContext db, DbContextGate gate) : IWidgetRepository
{
    public async Task<IReadOnlyList<Widget>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var widgets = await db.Widgets.AsNoTracking().ToListAsync(cancellationToken);
        return widgets.OrderBy(widget => widget.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Widget?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Widgets.AsNoTracking().FirstOrDefaultAsync(widget => widget.Id == id, cancellationToken);
    }

    public async Task<Widget?> GetByKeyAsync(string publicKey, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Widgets.AsNoTracking().FirstOrDefaultAsync(widget => widget.PublicKey == publicKey, cancellationToken);
    }

    public async Task<IReadOnlyList<Widget>> ListByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Widgets.AsNoTracking().Where(widget => widget.AgentId == agentId).ToListAsync(cancellationToken);
    }

    public Task<Widget> AddAsync(Widget widget, CancellationToken cancellationToken = default) => AddEntityAsync(widget, cancellationToken);

    public Task UpdateAsync(Widget widget, CancellationToken cancellationToken = default) => UpdateEntityAsync(widget, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var widget = await db.Widgets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (widget is null)
            {
                return false;
            }

            // The in-memory provider does not cascade, so the dependents go explicitly.
            db.WidgetAssets.RemoveRange(await db.WidgetAssets.Where(asset => asset.WidgetId == id).ToListAsync(cancellationToken));
            db.WidgetVisitors.RemoveRange(await db.WidgetVisitors.Where(visitor => visitor.WidgetId == id).ToListAsync(cancellationToken));
            db.WidgetDailyUsage.RemoveRange(await db.WidgetDailyUsage.Where(usage => usage.WidgetId == id).ToListAsync(cancellationToken));
            db.Widgets.Remove(widget);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public Task<WidgetAsset> AddAssetAsync(WidgetAsset asset, CancellationToken cancellationToken = default) => AddEntityAsync(asset, cancellationToken);

    public async Task<WidgetAsset?> GetAssetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.WidgetAssets.AsNoTracking().FirstOrDefaultAsync(asset => asset.Id == id, cancellationToken);
    }

    public async Task DeleteUnusedAssetsAsync(Guid widgetId, IReadOnlyCollection<Guid> keep, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var unused = await db.WidgetAssets
                .Where(asset => asset.WidgetId == widgetId && !keep.Contains(asset.Id))
                .ToListAsync(cancellationToken);
            if (unused.Count > 0)
            {
                db.WidgetAssets.RemoveRange(unused);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<WidgetVisitor?> GetVisitorAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.WidgetVisitors.AsNoTracking().FirstOrDefaultAsync(visitor => visitor.Id == id, cancellationToken);
    }

    public Task<WidgetVisitor> AddVisitorAsync(WidgetVisitor visitor, CancellationToken cancellationToken = default) => AddEntityAsync(visitor, cancellationToken);

    public Task UpdateVisitorAsync(WidgetVisitor visitor, CancellationToken cancellationToken = default) => UpdateEntityAsync(visitor, cancellationToken);

    public async Task<int> DeleteVisitorsSeenBeforeAsync(Guid widgetId, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var expired = db.WidgetVisitors.Where(visitor => visitor.WidgetId == widgetId && visitor.LastSeenAt < cutoff);
        if (db.Database.IsRelational())
        {
            return await expired.ExecuteDeleteAsync(cancellationToken);
        }

        try
        {
            var visitors = await expired.ToListAsync(cancellationToken);
            db.WidgetVisitors.RemoveRange(visitors);
            await db.SaveChangesAsync(cancellationToken);
            return visitors.Count;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<int> AddDailyMessageAsync(Guid widgetId, DateOnly day, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            // One statement, so instances counting at the same time never lose a message.
            var counts = await db.Database
                .SqlQuery<int>($"""
                    INSERT INTO widget_daily_usage (widget_id, day, messages) VALUES ({widgetId}, {day}, 1)
                    ON CONFLICT (widget_id, day) DO UPDATE SET messages = widget_daily_usage.messages + 1
                    RETURNING messages AS "Value"
                    """)
                .ToListAsync(cancellationToken);
            return counts.Single();
        }

        try
        {
            var usage = await db.WidgetDailyUsage.FirstOrDefaultAsync(item => item.WidgetId == widgetId && item.Day == day, cancellationToken);
            if (usage is null)
            {
                usage = new WidgetDailyUsage { WidgetId = widgetId, Day = day };
                db.WidgetDailyUsage.Add(usage);
            }

            usage.Messages++;
            await db.SaveChangesAsync(cancellationToken);
            return usage.Messages;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<T> AddEntityAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return entity;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task UpdateEntityAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Update(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
