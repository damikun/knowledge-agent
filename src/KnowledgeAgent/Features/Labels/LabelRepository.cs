using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Labels;

public sealed class LabelNameTakenException(string name) : Exception($"A label named '{name}' already exists.");

public interface ILabelRepository
{
    Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<Label?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Label>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Label>> ListAutoApplyAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken = default);

    Task<Label> AddAsync(Label label, CancellationToken cancellationToken = default);

    Task UpdateAsync(Label label, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationLabelDto>>> ForConversationsAsync(
        IReadOnlyCollection<Guid> conversationIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationLabel>> ListRowsAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationLabelDto>> SetAsync(Guid conversationId, IReadOnlyCollection<Guid> labelIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> ApplyAutoAsync(Guid conversationId, IReadOnlyCollection<Guid> labelIds, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ResetAutoLabeledAsync(DateTimeOffset updatedSince, int take, CancellationToken cancellationToken = default);
}

internal sealed class LabelRepository(KnowledgeDbContext db, DbContextGate gate) : ILabelRepository
{
    public async Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var labels = await db.Labels.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await db.ConversationLabels.AsNoTracking()
            .Where(item => !item.Removed)
            .GroupBy(item => item.LabelId)
            .Select(group => new { LabelId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.LabelId, item => item.Count, cancellationToken);
        return labels
            .OrderBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
            .Select(label => new LabelDto(
                label.Id,
                label.Name,
                label.Color,
                label.Description,
                label.AutoApply,
                counts.GetValueOrDefault(label.Id),
                label.CreatedAt,
                label.UpdatedAt))
            .ToList();
    }

    public async Task<Label?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Labels.AsNoTracking().FirstOrDefaultAsync(label => label.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Label>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Labels.AsNoTracking().Where(label => ids.Contains(label.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Label>> ListAutoApplyAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var labels = await db.Labels.AsNoTracking().Where(label => label.AutoApply).ToListAsync(cancellationToken);
        return labels.OrderBy(label => label.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Labels.AsNoTracking().AnyAsync(cancellationToken);
    }

    public async Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken = default)
    {
        var normalized = Label.Normalize(name);
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Labels.AsNoTracking().AnyAsync(label => label.NormalizedName == normalized && label.Id != exceptId, cancellationToken);
    }

    public async Task<Label> AddAsync(Label label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.NormalizedName = Label.Normalize(label.Name);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!db.Database.IsRelational() && await db.Labels.AnyAsync(other => other.NormalizedName == label.NormalizedName, cancellationToken))
            {
                // The in-memory store does not enforce unique indexes.
                throw new LabelNameTakenException(label.Name);
            }

            db.Labels.Add(label);
            await db.SaveChangesAsync(cancellationToken);
            return label;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new LabelNameTakenException(label.Name);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateAsync(Label label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        label.NormalizedName = Label.Normalize(label.Name);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!db.Database.IsRelational()
                && await db.Labels.AnyAsync(other => other.NormalizedName == label.NormalizedName && other.Id != label.Id, cancellationToken))
            {
                throw new LabelNameTakenException(label.Name);
            }

            db.Labels.Update(label);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Label '{label.Id}' does not exist.", ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new LabelNameTakenException(label.Name);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsRelational())
        {
            return await db.Labels.Where(label => label.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        try
        {
            var label = await db.Labels.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (label is null)
            {
                return false;
            }

            db.ConversationLabels.RemoveRange(await db.ConversationLabels.Where(item => item.LabelId == id).ToListAsync(cancellationToken));
            db.Labels.Remove(label);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationLabelDto>>> ForConversationsAsync(
        IReadOnlyCollection<Guid> conversationIds,
        CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await ConversationLabelQueries.LoadAsync(db, conversationIds, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationLabel>> ListRowsAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.ConversationLabels.AsNoTracking().Where(item => item.ConversationId == conversationId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationLabelDto>> SetAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid> labelIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!await db.Conversations.AnyAsync(conversation => conversation.Id == conversationId, cancellationToken))
            {
                throw new KeyNotFoundException($"Conversation '{conversationId}' does not exist.");
            }

            var wanted = labelIds.ToHashSet();
            var rows = await db.ConversationLabels.Where(item => item.ConversationId == conversationId).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (wanted.Contains(row.LabelId))
                {
                    if (row.Removed)
                    {
                        row.Removed = false;
                        row.Source = LabelSource.Manual;
                    }
                }
                else if (!row.Removed)
                {
                    if (row.Source == LabelSource.Auto)
                    {
                        row.Removed = true;
                    }
                    else
                    {
                        db.ConversationLabels.Remove(row);
                    }
                }
            }

            foreach (var labelId in wanted.Except(rows.Select(row => row.LabelId)))
            {
                db.ConversationLabels.Add(new ConversationLabel
                {
                    ConversationId = conversationId,
                    LabelId = labelId,
                    Source = LabelSource.Manual,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var labels = await ConversationLabelQueries.LoadAsync(db, [conversationId], cancellationToken);
            return labels.TryGetValue(conversationId, out var found) ? found : [];
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // The conversation or a label was deleted meanwhile.
            throw new KeyNotFoundException($"Conversation '{conversationId}' or one of its labels does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> ApplyAutoAsync(Guid conversationId, IReadOnlyCollection<Guid> labelIds, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var conversation = await db.Conversations.FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
            if (conversation is null)
            {
                return false;
            }

            var picked = labelIds.ToHashSet();
            var rows = await db.ConversationLabels.Where(item => item.ConversationId == conversationId).ToListAsync(cancellationToken);
            db.ConversationLabels.RemoveRange(rows.Where(row => row.Source == LabelSource.Auto && !row.Removed && !picked.Contains(row.LabelId)));
            foreach (var labelId in picked.Except(rows.Select(row => row.LabelId)))
            {
                db.ConversationLabels.Add(new ConversationLabel
                {
                    ConversationId = conversationId,
                    LabelId = labelId,
                    Source = LabelSource.Auto,
                    CreatedAt = now,
                });
            }

            conversation.AutoLabeledAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || IsForeignKeyViolation(ex) || IsUniqueViolation(ex))
        {
            // Deleted meanwhile, or staff set labels at the same moment: their choice stands.
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<Guid>> ResetAutoLabeledAsync(DateTimeOffset updatedSince, int take, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var ids = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.UpdatedAt >= updatedSince && conversation.Messages.Any())
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Select(conversation => conversation.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return ids;
        }

        if (db.Database.IsRelational())
        {
            await db.Conversations.Where(conversation => ids.Contains(conversation.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(conversation => conversation.AutoLabeledAt, (DateTimeOffset?)null), cancellationToken);
            return ids;
        }

        try
        {
            foreach (var conversation in await db.Conversations.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken))
            {
                conversation.AutoLabeledAt = null;
            }

            await db.SaveChangesAsync(cancellationToken);
            return ids;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}

// Shared with the conversation lists and search, which call it while holding the context's gate.
internal static class ConversationLabelQueries
{
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationLabelDto>>> LoadAsync(
        KnowledgeDbContext db,
        IReadOnlyCollection<Guid> conversationIds,
        CancellationToken cancellationToken)
    {
        if (conversationIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ConversationLabelDto>>();
        }

        var ids = conversationIds as ICollection<Guid> ?? conversationIds.ToList();
        var rows = await db.ConversationLabels.AsNoTracking()
            .Where(item => ids.Contains(item.ConversationId) && !item.Removed)
            .Join(db.Labels, item => item.LabelId, label => label.Id, (item, label) => new { item.ConversationId, label.Id, label.Name, label.Color, item.Source })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.ConversationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConversationLabelDto>)group
                    .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new ConversationLabelDto(row.Id, row.Name, row.Color, ToText(row.Source)))
                    .ToList());
    }

    public static string ToText(LabelSource source) => source == LabelSource.Auto ? "auto" : "manual";
}
