using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KnowledgeAgent.Features.Chat;

internal sealed partial class ConversationRepository(
    KnowledgeDbContext db,
    DbContextGate gate,
    TimeProvider timeProvider,
    ILogger<ConversationRepository> logger) : IConversationRepository
{
    // Every lost race means another writer committed a turn, so N concurrent writers need at most N attempts.
    private const int MaxSequenceAttempts = 10;

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        AssignMissingSequences(conversation.Turns);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            db.Conversations.Add(conversation);
            if (conversation.ContactId is { } contactId && await db.Contacts.FirstOrDefaultAsync(contact => contact.Id == contactId, cancellationToken) is { } contact)
            {
                contact.ConversationCount++;
                contact.LastSeenAt = contact.LastSeenAt > conversation.CreatedAt ? contact.LastSeenAt : conversation.CreatedAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            return conversation;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<Conversation?> GetAsync(Guid id, bool includeTurns = true, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var conversation = await db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (conversation is null || !includeTurns)
        {
            return conversation;
        }

        // Separate queries instead of Include, because a join would repeat the large system prompt on every row.
        conversation.Turns = await db.ConversationTurns.AsNoTracking()
            .Where(turn => turn.ConversationId == id)
            .OrderBy(turn => turn.Sequence)
            .ToListAsync(cancellationToken);
        conversation.Messages = await db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == id)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
        return conversation;
    }

    public async Task<Guid?> FindConversationOfMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.ConversationMessages.AsNoTracking()
            .Where(message => message.Id == messageId)
            .Select(message => (Guid?)message.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> FindLatestForVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.VisitorId == visitorId && conversation.Status != ConversationStatus.Closed)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Select(conversation => (Guid?)conversation.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> FindLatestForContactAsync(Guid contactId, Guid widgetId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.ContactId == contactId && conversation.WidgetId == widgetId
                && conversation.Channel == ConversationChannel.Widget && conversation.Status != ConversationStatus.Closed)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .Select(conversation => (Guid?)conversation.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> DeleteWidgetConversationsAsync(Guid widgetId, DateTimeOffset updatedBefore, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var expired = db.Conversations.Where(conversation => conversation.WidgetId == widgetId && conversation.UpdatedAt < updatedBefore);
        if (db.Database.IsRelational())
        {
            return await expired.ExecuteDeleteAsync(cancellationToken);
        }

        try
        {
            var conversations = await expired.Include(item => item.Turns).Include(item => item.Messages).ToListAsync(cancellationToken);
            var ids = conversations.Select(conversation => conversation.Id).ToList();
            db.ConversationLabels.RemoveRange(await db.ConversationLabels.Where(item => ids.Contains(item.ConversationId)).ToListAsync(cancellationToken));
            db.Conversations.RemoveRange(conversations);
            await db.SaveChangesAsync(cancellationToken);
            return conversations.Count;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<Guid>> ListIdleAsync(
        Guid widgetId,
        ConversationStatus status,
        DateTimeOffset updatedBefore,
        IReadOnlyCollection<MessageAuthor>? lastAuthors,
        int take,
        CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var idle = db.Conversations.AsNoTracking()
            .Where(conversation => conversation.WidgetId == widgetId && conversation.Channel == ConversationChannel.Widget
                && conversation.Status == status && conversation.UpdatedAt < updatedBefore);
        if (lastAuthors is not null)
        {
            idle = idle.Where(conversation => lastAuthors.Contains(
                conversation.Messages.OrderByDescending(message => message.Sequence).Select(message => message.Author).FirstOrDefault()));
        }

        return await idle
            .OrderBy(conversation => conversation.UpdatedAt)
            .ThenBy(conversation => conversation.Id)
            .Select(conversation => conversation.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var assignSequence = turn.Sequence <= 0;
        using var _ = await gate.EnterAsync(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var conversation = await db.Conversations
                    .Where(item => item.Id == turn.ConversationId)
                    .Select(item => new { MaxSequence = item.Turns.Max(existing => (int?)existing.Sequence) })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException($"Conversation '{turn.ConversationId}' does not exist.");

                if (assignSequence)
                {
                    turn.Sequence = (conversation.MaxSequence ?? 0) + 1;
                }

                db.ConversationTurns.Add(turn);
                var touched = db.Conversations.Attach(new Conversation { Id = turn.ConversationId, UpdatedAt = timeProvider.GetUtcNow() });
                touched.Property(item => item.UpdatedAt).IsModified = true;

                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (assignSequence && attempt < MaxSequenceAttempts && IsSequenceConflict(ex, ConversationTurnConfiguration.SequenceIndexName))
            {
                LogSequenceConflict(logger, turn.ConversationId, turn.Sequence, attempt);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || IsForeignKeyViolation(ex))
            {
                // Deleted after the existence check: the UPDATE matched no row or the INSERT hit the foreign key.
                throw new KeyNotFoundException($"Conversation '{turn.ConversationId}' does not exist.", ex);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<ConversationMessage> AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var _ = await gate.EnterAsync(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var conversation = await db.Conversations
                    .Where(item => item.Id == message.ConversationId)
                    .Select(item => new { MaxSequence = item.Messages.Max(existing => (int?)existing.Sequence) })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new KeyNotFoundException($"Conversation '{message.ConversationId}' does not exist.");

                message.Sequence = (conversation.MaxSequence ?? 0) + 1;
                db.ConversationMessages.Add(message);
                var touched = db.Conversations.Attach(new Conversation { Id = message.ConversationId, UpdatedAt = timeProvider.GetUtcNow() });
                touched.Property(item => item.UpdatedAt).IsModified = true;

                await db.SaveChangesAsync(cancellationToken);
                return message;
            }
            catch (DbUpdateException ex) when (attempt < MaxSequenceAttempts && IsSequenceConflict(ex, ConversationMessageConfiguration.SequenceIndexName))
            {
                LogSequenceConflict(logger, message.ConversationId, message.Sequence, attempt);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || IsForeignKeyViolation(ex))
            {
                throw new KeyNotFoundException($"Conversation '{message.ConversationId}' does not exist.", ex);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid conversationId, int afterSequence = 0, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.Sequence > afterSequence)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid id, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var entry = db.Conversations.Attach(new Conversation { Id = id, Status = status, UpdatedAt = timeProvider.GetUtcNow() });
            entry.Property(conversation => conversation.Status).IsModified = true;
            entry.Property(conversation => conversation.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Conversation '{id}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> TryUpdateStatusAsync(
        Guid id,
        ConversationStatus expected,
        ConversationStatus status,
        DateTimeOffset? updatedBefore = null,
        CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var matching = db.Conversations.Where(conversation => conversation.Id == id && conversation.Status == expected);
        if (updatedBefore is { } cutoff)
        {
            matching = matching.Where(conversation => conversation.UpdatedAt < cutoff);
        }

        if (db.Database.IsRelational())
        {
            // One statement, so a message written at the same moment cannot slip in between the check and the change.
            return await matching.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(conversation => conversation.Status, status)
                    .SetProperty(conversation => conversation.UpdatedAt, now),
                cancellationToken) == 1;
        }

        // Non-relational providers have no ExecuteUpdate.
        try
        {
            if (await matching.FirstOrDefaultAsync(cancellationToken) is not { } conversation)
            {
                return false;
            }

            conversation.Status = status;
            conversation.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateTitleAsync(Guid id, string title, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            // UpdatedAt stays unchanged so renaming does not reorder the conversation list.
            var entry = db.Conversations.Attach(new Conversation { Id = id, Title = title });
            entry.Property(conversation => conversation.Title).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Conversation '{id}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task UpdateTurnToolCallsAsync(Guid turnId, string toolCallsJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCallsJson);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var entry = db.ConversationTurns.Attach(new ConversationTurn { Id = turnId, ToolCallsJson = toolCallsJson });
            entry.Property(turn => turn.ToolCallsJson).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Turn '{turnId}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task SetActiveAgentAsync(Guid id, Guid? agentId, string? agentName, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var entry = db.Conversations.Attach(new Conversation { Id = id, ActiveAgentId = agentId, ActiveAgentName = agentId is null ? null : agentName });
            entry.Property(conversation => conversation.ActiveAgentId).IsModified = true;
            entry.Property(conversation => conversation.ActiveAgentName).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Conversation '{id}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var entry = db.Conversations.Attach(new Conversation { Id = id, Pinned = pinned });
            entry.Property(conversation => conversation.Pinned).IsModified = true;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
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
            var contactId = await db.Conversations.Where(conversation => conversation.Id == id).Select(conversation => conversation.ContactId).FirstOrDefaultAsync(cancellationToken);
            var deleted = await db.Conversations.Where(conversation => conversation.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
            if (deleted && contactId is { } relationalContact)
            {
                await db.Contacts.Where(contact => contact.Id == relationalContact && contact.ConversationCount > 0)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(contact => contact.ConversationCount, contact => contact.ConversationCount - 1), cancellationToken);
            }

            return deleted;
        }

        // Non-relational providers lack ExecuteDelete and store-side cascades, so delete through the change tracker.
        try
        {
            var conversation = await db.Conversations
                .Include(item => item.Turns)
                .Include(item => item.Messages)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (conversation is null)
            {
                return false;
            }

            db.ConversationLabels.RemoveRange(await db.ConversationLabels.Where(item => item.ConversationId == id).ToListAsync(cancellationToken));
            db.Conversations.Remove(conversation);
            if (conversation.ContactId is { } contactId && await db.Contacts.FirstOrDefaultAsync(contact => contact.Id == contactId, cancellationToken) is { ConversationCount: > 0 } contact)
            {
                contact.ConversationCount--;
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static void AssignMissingSequences(List<ConversationTurn> turns)
    {
        if (turns.Count == 0)
        {
            return;
        }

        var next = Math.Max(0, turns.Max(turn => turn.Sequence)) + 1;
        foreach (var turn in turns.Where(turn => turn.Sequence <= 0))
        {
            turn.Sequence = next++;
        }
    }

    private static bool IsSequenceConflict(DbUpdateException exception, string indexName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == indexName;

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Sequence {Sequence} of conversation {ConversationId} was taken by a concurrent writer; retrying (attempt {Attempt})")]
    private static partial void LogSequenceConflict(ILogger logger, Guid conversationId, int sequence, int attempt);
}
