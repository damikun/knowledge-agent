using System.Linq.Expressions;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Conversations;

public sealed record ConversationReadDto(int LastReadSequence, bool Unread);

public sealed record MarkReadRequest(int? Sequence = null);

public interface IConversationReads
{
    // Never moves back, so a late request does not undo a newer one; null when the conversation does not exist.
    Task<ConversationReadDto?> MarkReadAsync(string userId, Guid conversationId, int? sequence, CancellationToken cancellationToken = default);

    Task<ConversationReadDto?> MarkUnreadAsync(string userId, Guid conversationId, CancellationToken cancellationToken = default);
}

internal static class UnreadQueries
{
    // Messages from before someone first signed in count as read, so a new colleague doesn't start with everything unread.
    public static async Task<DateTimeOffset> SinceAsync(KnowledgeDbContext db, string viewerId, CancellationToken cancellationToken) =>
        await db.StaffMembers.AsNoTracking()
            .Where(member => member.Id == viewerId)
            .Select(member => (DateTimeOffset?)member.FirstSeenAt)
            .FirstOrDefaultAsync(cancellationToken) ?? DateTimeOffset.UnixEpoch;

    public static Expression<Func<ConversationMessage, bool>> Counts =>
        message => message.Author == MessageAuthor.Visitor || message.Author == MessageAuthor.Assistant;

    public static Expression<Func<Conversation, bool>> Unread(KnowledgeDbContext db, string viewerId, DateTimeOffset since) =>
        conversation => db.ConversationMessages.Any(message =>
            message.ConversationId == conversation.Id
            && (message.Author == MessageAuthor.Visitor || message.Author == MessageAuthor.Assistant)
            && (db.ConversationReads.Any(read => read.UserId == viewerId && read.ConversationId == conversation.Id && read.LastReadSequence < message.Sequence)
                || (message.CreatedAt >= since && !db.ConversationReads.Any(read => read.UserId == viewerId && read.ConversationId == conversation.Id))));
}

internal sealed class ConversationReadRepository(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : IConversationReads
{
    public async Task<ConversationReadDto?> MarkReadAsync(string userId, Guid conversationId, int? sequence, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (await LatestAsync(conversationId, cancellationToken) is not { } latest)
        {
            return null;
        }

        // Never past the last message, so a message that arrives later is not read in advance.
        var target = Math.Clamp(sequence ?? latest.Any, 0, latest.Any);
        var stored = await SaveAsync(userId, conversationId, target, forward: true, cancellationToken);
        return new ConversationReadDto(stored, latest.Counted > stored);
    }

    public async Task<ConversationReadDto?> MarkUnreadAsync(string userId, Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (await LatestAsync(conversationId, cancellationToken) is not { } latest)
        {
            return null;
        }

        var target = Math.Max(0, latest.Counted - 1);
        var stored = await SaveAsync(userId, conversationId, target, forward: false, cancellationToken);
        return new ConversationReadDto(stored, latest.Counted > stored);
    }

    private async Task<(int Any, int Counted)?> LatestAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var row = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.Id == conversationId)
            .Select(conversation => new
            {
                Any = db.ConversationMessages.Where(message => message.ConversationId == conversationId).Max(message => (int?)message.Sequence),
                Counted = db.ConversationMessages
                    .Where(message => message.ConversationId == conversationId)
                    .Where(UnreadQueries.Counts)
                    .Max(message => (int?)message.Sequence),
            })
            .ToListAsync(cancellationToken);
        return row is [var found] ? (found.Any ?? 0, found.Counted ?? 0) : null;
    }

    private async Task<int> SaveAsync(string userId, Guid conversationId, int sequence, bool forward, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (db.Database.IsNpgsql())
        {
            // One statement, so two tabs reading at once neither fail nor move the position back.
            if (forward)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO conversation_reads (user_id, conversation_id, last_read_sequence, updated_at) VALUES ({userId}, {conversationId}, {sequence}, {now})
                    ON CONFLICT (user_id, conversation_id) DO UPDATE
                    SET last_read_sequence = GREATEST(conversation_reads.last_read_sequence, EXCLUDED.last_read_sequence), updated_at = EXCLUDED.updated_at
                    """,
                    cancellationToken);
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO conversation_reads (user_id, conversation_id, last_read_sequence, updated_at) VALUES ({userId}, {conversationId}, {sequence}, {now})
                    ON CONFLICT (user_id, conversation_id) DO UPDATE SET last_read_sequence = EXCLUDED.last_read_sequence, updated_at = EXCLUDED.updated_at
                    """,
                    cancellationToken);
            }

            return await db.ConversationReads.AsNoTracking()
                .Where(read => read.UserId == userId && read.ConversationId == conversationId)
                .Select(read => read.LastReadSequence)
                .FirstAsync(cancellationToken);
        }

        try
        {
            var read = await db.ConversationReads.FirstOrDefaultAsync(item => item.UserId == userId && item.ConversationId == conversationId, cancellationToken);
            if (read is null)
            {
                read = new ConversationRead { UserId = userId, ConversationId = conversationId, LastReadSequence = sequence, UpdatedAt = now };
                db.ConversationReads.Add(read);
            }
            else
            {
                read.LastReadSequence = forward ? Math.Max(read.LastReadSequence, sequence) : sequence;
                read.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
            return read.LastReadSequence;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
