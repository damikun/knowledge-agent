using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KnowledgeAgent.Features.Feedback;

public sealed record FeedbackFilter(
    Guid? WidgetId = null,
    Guid? AgentId = null,
    FeedbackKind? Kind = null,
    bool? Positive = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public interface IFeedbackRepository
{
    Task<Feedback?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Feedback?> FindAsync(Guid conversationId, Guid? messageId, FeedbackKind kind, string authorKey, CancellationToken cancellationToken = default);

    Task<Feedback> SaveAsync(Feedback feedback, CancellationToken cancellationToken = default);
    Task MarkScoreSentAsync(Guid id, DateTimeOffset sentAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Feedback>> ListAsync(FeedbackFilter filter, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Feedback>> ListByAuthorAsync(Guid conversationId, string authorKey, CancellationToken cancellationToken = default);
}

internal sealed class FeedbackRepository(KnowledgeDbContext db, DbContextGate gate) : IFeedbackRepository
{
    public async Task<Feedback?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Feedback.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<Feedback?> FindAsync(Guid conversationId, Guid? messageId, FeedbackKind kind, string authorKey, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Feedback.AsNoTracking().FirstOrDefaultAsync(
            item => item.ConversationId == conversationId && item.MessageId == messageId && item.Kind == kind && item.AuthorKey == authorKey,
            cancellationToken);
    }

    public async Task<Feedback> SaveAsync(Feedback feedback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var exists = await db.Feedback.AsNoTracking().AnyAsync(item => item.Id == feedback.Id, cancellationToken);
            if (exists)
            {
                db.Feedback.Update(feedback);
            }
            else
            {
                db.Feedback.Add(feedback);
            }

            await db.SaveChangesAsync(cancellationToken);
            return feedback;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task MarkScoreSentAsync(Guid id, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (await db.Feedback.FirstOrDefaultAsync(item => item.Id == id, cancellationToken) is { } feedback)
            {
                feedback.ScoreSentAt = sentAt;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<Feedback>> ListAsync(FeedbackFilter filter, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var _ = await gate.EnterAsync(cancellationToken);
        var query = db.Feedback.AsNoTracking();
        if (filter.WidgetId is { } widgetId)
        {
            query = query.Where(item => item.WidgetId == widgetId);
        }

        if (filter.AgentId is { } agentId)
        {
            query = query.Where(item => item.AgentId == agentId);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(item => item.Kind == kind);
        }

        if (filter.Positive is { } positive)
        {
            query = positive
                ? query.Where(item => (item.Kind == FeedbackKind.Conversation && item.Rating >= 4) || (item.Kind != FeedbackKind.Conversation && item.Rating > 0))
                : query.Where(item => (item.Kind == FeedbackKind.Conversation && item.Rating <= 2) || (item.Kind != FeedbackKind.Conversation && item.Rating < 0));
        }

        if (filter.From is { } from)
        {
            query = query.Where(item => item.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(item => item.CreatedAt < to);
        }

        return await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Feedback>> ListByAuthorAsync(Guid conversationId, string authorKey, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Feedback.AsNoTracking()
            .Where(item => item.ConversationId == conversationId && item.AuthorKey == authorKey)
            .ToListAsync(cancellationToken);
    }
}

internal sealed class FeedbackConfiguration(bool isNpgsql) : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedback");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.ConversationId, item.MessageId, item.Kind, item.AuthorKey });
        builder.HasIndex(item => item.CreatedAt);
        builder.HasIndex(item => new { item.WidgetId, item.CreatedAt });
        builder.Property(item => item.Kind).HasMaxLength(16);
        builder.Property(item => item.Author).HasMaxLength(16);
        builder.Property(item => item.AuthorKey).HasMaxLength(200);
        builder.Property(item => item.Comment).HasMaxLength(Feedback.MaxCommentLength);
        builder.Property(item => item.TraceId).HasMaxLength(32);

        var reasons = builder.Property(item => item.Reasons)
            .HasConversion(JsonColumns.StringList, JsonColumns.StringListComparer)
            .IsRequired();
        if (isNpgsql)
        {
            reasons.HasColumnType("jsonb");
        }

        builder.HasOne<Chat.Conversation>().WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
