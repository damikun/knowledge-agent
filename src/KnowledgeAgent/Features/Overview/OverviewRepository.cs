using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Overview;

public interface IOverviewRepository
{
    Task<OverviewDto> GetAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default);
}

internal sealed class OverviewRepository(KnowledgeDbContext db, DbContextGate gate) : IOverviewRepository
{
    public async Task<OverviewDto> GetAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default)
    {
        var from = now.AddDays(-days);
        var previousFrom = now.AddDays(-2 * days);
        using var _ = await gate.EnterAsync(cancellationToken);

        var started = StartedSince(from);
        var byChannel = await started
            .GroupBy(conversation => conversation.Channel)
            .Select(group => new { Channel = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var conversations = byChannel.Sum(item => item.Count);
        var website = byChannel.Where(item => item.Channel == ConversationChannel.Widget).Sum(item => item.Count);

        var handedToPerson = await started.CountAsync(
            conversation => db.Handovers.Any(handover => handover.ConversationId == conversation.Id && handover.Status != HandoverStatus.Cancelled),
            cancellationToken);
        var previous = await StartedSince(previousFrom).CountAsync(conversation => conversation.CreatedAt < from, cancellationToken);

        var ratings = db.Feedback.AsNoTracking().Where(item => item.Kind == FeedbackKind.Conversation && item.CreatedAt >= from
            && !db.Conversations.Any(conversation => conversation.Id == item.ConversationId && conversation.Channel == ConversationChannel.Portal));
        var ratingCount = await ratings.CountAsync(cancellationToken);
        var averageRating = ratingCount == 0 ? null : await ratings.AverageAsync(item => (double?)item.Rating, cancellationToken);

        // One query: each widget's numbers are subqueries served by the (widget_id, updated_at) index.
        var widgets = await db.Widgets.AsNoTracking()
            .Select(widget => new WidgetActivityDto(
                widget.Id,
                db.Conversations.Count(conversation =>
                    conversation.WidgetId == widget.Id && conversation.Channel == ConversationChannel.Widget
                    && conversation.UpdatedAt >= from && conversation.CreatedAt >= from),
                db.Conversations
                    .Where(conversation => conversation.WidgetId == widget.Id && conversation.Channel == ConversationChannel.Widget)
                    .OrderByDescending(conversation => conversation.UpdatedAt)
                    .Select(conversation => (DateTimeOffset?)conversation.UpdatedAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new OverviewDto(
            days,
            from,
            conversations,
            website,
            previous,
            conversations - handedToPerson,
            handedToPerson,
            averageRating is { } average ? Math.Round(average, 2) : null,
            ratingCount,
            widgets);
    }

    // UpdatedAt is never before CreatedAt, so the UpdatedAt index narrows each count to the conversations active since then.
    // Portal conversations are playground tests and do not count.
    private IQueryable<Conversation> StartedSince(DateTimeOffset since) =>
        db.Conversations.AsNoTracking().Where(conversation =>
            conversation.UpdatedAt >= since && conversation.CreatedAt >= since && conversation.Channel != ConversationChannel.Portal);
}
