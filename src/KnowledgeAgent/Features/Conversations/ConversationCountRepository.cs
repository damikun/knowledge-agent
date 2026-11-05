using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Conversations;

public sealed record ConversationCountQuery
{
    public ConversationChannel? Channel { get; init; }
    public Guid? WidgetId { get; init; }
    public Guid? ContactId { get; init; }
    public bool IncludeTests { get; init; }

    public string? ViewerId { get; init; }
}

public sealed record ConversationCountsDto(int HandoverPending, int Human, int Ai, int Closed, int All, IReadOnlyList<LabelCountDto> Labels)
{
    public int Pinned { get; init; }

    public int Mine { get; init; }
    public int Unassigned { get; init; }

    public UnreadCountsDto Unread { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record UnreadCountsDto(int HandoverPending, int Human, int Ai, int Closed, int All, int Pinned, int Mine, int Unassigned);

public sealed record LabelCountDto(Guid LabelId, int Count);

public interface IConversationCounts
{
    Task<ConversationCountsDto> CountAsync(ConversationCountQuery query, CancellationToken cancellationToken = default);
}

internal sealed class ConversationCountRepository(KnowledgeDbContext db, DbContextGate gate) : IConversationCounts
{
    public async Task<ConversationCountsDto> CountAsync(ConversationCountQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conversations = db.Conversations.AsNoTracking();
        if (query.Channel is { } channel)
        {
            conversations = conversations.Where(conversation => conversation.Channel == channel);
        }
        else if (!query.IncludeTests)
        {
            conversations = conversations.Where(conversation => conversation.Channel != ConversationChannel.Portal);
        }

        if (query.WidgetId is { } widgetId)
        {
            conversations = conversations.Where(conversation => conversation.WidgetId == widgetId);
        }

        if (query.ContactId is { } contactId)
        {
            conversations = conversations.Where(conversation => conversation.ContactId == contactId);
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        // A few grouped counts, so the rail costs a few small queries however many views it shows.
        var viewer = query.ViewerId;
        var byStatus = await Grouped(conversations, viewer).ToListAsync(cancellationToken);
        var unread = viewer is null
            ? new List<CountRow>()
            : await Grouped(conversations.Where(UnreadQueries.Unread(db, viewer, await UnreadQueries.SinceAsync(db, viewer, cancellationToken))), viewer)
                .ToListAsync(cancellationToken);
        var byLabel = await db.ConversationLabels.AsNoTracking()
            .Where(item => !item.Removed && conversations.Any(conversation => conversation.Id == item.ConversationId))
            .GroupBy(item => item.LabelId)
            .Select(group => new { LabelId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Of(ConversationStatus status) => byStatus.Where(row => row.Status == status).Sum(row => row.Count);
        static int Mine(List<CountRow> rows) => rows.Where(row => row.Mine && row.Status != ConversationStatus.Closed).Sum(row => row.Count);
        static int Unassigned(List<CountRow> rows) =>
            rows.Where(row => row.Unassigned && row.Status is ConversationStatus.HandoverPending or ConversationStatus.Human).Sum(row => row.Count);
        int UnreadOf(ConversationStatus status) => unread.Where(row => row.Status == status).Sum(row => row.Count);
        return new ConversationCountsDto(
            Of(ConversationStatus.HandoverPending),
            Of(ConversationStatus.Human),
            Of(ConversationStatus.Ai),
            Of(ConversationStatus.Closed),
            byStatus.Sum(row => row.Count),
            byLabel.OrderByDescending(row => row.Count).ThenBy(row => row.LabelId).Select(row => new LabelCountDto(row.LabelId, row.Count)).ToList())
        {
            Pinned = byStatus.Where(row => row.Pinned).Sum(row => row.Count),
            Mine = Mine(byStatus),
            Unassigned = Unassigned(byStatus),
            Unread = new UnreadCountsDto(
                UnreadOf(ConversationStatus.HandoverPending),
                UnreadOf(ConversationStatus.Human),
                UnreadOf(ConversationStatus.Ai),
                UnreadOf(ConversationStatus.Closed),
                unread.Sum(row => row.Count),
                unread.Where(row => row.Pinned).Sum(row => row.Count),
                Mine(unread),
                Unassigned(unread)),
        };
    }

    private static IQueryable<CountRow> Grouped(IQueryable<Conversation> conversations, string? viewer) =>
        conversations
            .GroupBy(conversation => new
            {
                conversation.Status,
                conversation.Pinned,
                Mine = viewer != null && conversation.AssigneeId == viewer,
                Unassigned = conversation.AssigneeName == null,
            })
            .Select(group => new CountRow(group.Key.Status, group.Key.Pinned, group.Key.Mine, group.Key.Unassigned, group.Count()));

    private sealed record CountRow(ConversationStatus Status, bool Pinned, bool Mine, bool Unassigned, int Count);
}
