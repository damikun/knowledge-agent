using System.Linq.Expressions;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Conversations;

public interface IConversationSearch
{
    Task<ConversationSearchPageDto> SearchAsync(ConversationSearchQuery query, CancellationToken cancellationToken = default);

    Task<ConversationListPageDto> ListAsync(ConversationSearchQuery query, CancellationToken cancellationToken = default);

    Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

internal sealed class ConversationSearchRepository(KnowledgeDbContext db, DbContextGate gate, IPeopleRepository people) : IConversationSearch
{
    private const int PreviewLength = 160;
    private const int MatchLength = 200;

    public async Task<ConversationSearchPageDto> SearchAsync(ConversationSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, ConversationSearchQuery.MaxLimit);
        var terms = LikeSearch.Terms(query.Text);
        var npgsql = db.Database.IsNpgsql();
        var conversations = Ordered(After(WithoutTests(await InSegmentAsync(Matching(db.Conversations.AsNoTracking(), query, terms, npgsql), query, cancellationToken), query), query), query);

        using var _ = await gate.EnterAsync(cancellationToken);
        var (viewed, since) = await ForViewerAsync(conversations, query, cancellationToken);
        var rows = await viewed
            .Take(limit + 1)
            .Select(conversation => new
            {
                conversation.Id,
                conversation.Title,
                conversation.Channel,
                conversation.Status,
                conversation.AgentId,
                conversation.WidgetId,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.Pinned,
                conversation.CreatedById,
                conversation.CreatedByName,
                Contact = db.Contacts
                    .Where(contact => contact.Id == conversation.ContactId)
                    .Select(contact => new ContactRow(contact.Id, contact.Name, contact.Email, contact.ExternalId))
                    .FirstOrDefault(),
                MessageCount = conversation.Messages.Count,
                Preview = conversation.Messages
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Text.Length > PreviewLength ? message.Text.Substring(0, PreviewLength) : message.Text)
                    .FirstOrDefault(),
                Handover = db.Handovers
                    .Where(handover => handover.ConversationId == conversation.Id)
                    .OrderByDescending(handover => handover.RequestedAt)
                    .Select(handover => (HandoverStatus?)handover.Status)
                    .FirstOrDefault(),
                Rating = db.Feedback
                    .Where(item => item.ConversationId == conversation.Id && item.Kind == FeedbackKind.Conversation)
                    .OrderByDescending(item => item.UpdatedAt)
                    .Select(item => (int?)item.Rating)
                    .FirstOrDefault(),
                ThumbsDown = db.Feedback.Count(item => item.ConversationId == conversation.Id && item.Kind == FeedbackKind.Answer && item.Rating < 0),
                conversation.AssigneeId,
                conversation.AssigneeName,
            })
            .ToListAsync(cancellationToken);

        var page = rows.Take(limit).ToList();
        var pageIds = page.Select(row => row.Id).ToList();
        var matches = terms.Count == 0 ? null : await FirstMatchesAsync(pageIds, terms[0], npgsql, cancellationToken);
        var unreadIds = await UnreadAmongAsync(pageIds, query.ViewerId, since, cancellationToken);
        var labels = await ConversationLabelQueries.LoadAsync(db, pageIds, cancellationToken);
        var next = rows.Count > limit
            ? new ConversationCursor(query.Sort == ConversationSortKey.Created ? page[^1].CreatedAt : page[^1].UpdatedAt, page[^1].Id).Encode()
            : null;
        return new ConversationSearchPageDto(
            page.Select(row => new ConversationSearchItemDto(
                    row.Id,
                    row.Title,
                    row.Channel.ToString().ToLowerInvariant(),
                    ConversationStatuses.ToText(row.Status),
                    row.AgentId,
                    row.WidgetId,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.MessageCount,
                    row.Preview,
                    matches is not null && matches.TryGetValue(row.Id, out var match) ? Passage(match, terms[0]) : null,
                    row.Handover?.ToString().ToLowerInvariant(),
                    row.Rating,
                    row.ThumbsDown)
                {
                    Pinned = row.Pinned,
                    Labels = labels.TryGetValue(row.Id, out var found) ? found : [],
                    Contact = row.Contact?.ToDto(),
                    CreatedBy = CreatorOf(row.CreatedById, row.CreatedByName),
                    Assignee = ConversationAssigneeDto.Of(row.AssigneeId, row.AssigneeName),
                    Unread = unreadIds.Contains(row.Id),
                })
                .ToList(),
            next);
    }

    public async Task<ConversationListPageDto> ListAsync(ConversationSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, ConversationSearchQuery.MaxLimit);
        var terms = LikeSearch.Terms(query.Text);
        var npgsql = db.Database.IsNpgsql();
        var conversations = await InSegmentAsync(Matching(db.Conversations.AsNoTracking(), query, terms, npgsql), query, cancellationToken);
        if (query.After is { } after)
        {
            var (value, id) = (after.Value, after.Id);

            // Pinned ones come first, so after a pinned conversation the rest of the pinned ones and every unpinned one follow.
            conversations = after.Pinned == true
                ? conversations.Where(conversation => !conversation.Pinned
                    || conversation.UpdatedAt < value
                    || (conversation.UpdatedAt == value && conversation.Id.CompareTo(id) < 0))
                : conversations.Where(conversation => !conversation.Pinned
                    && (conversation.UpdatedAt < value || (conversation.UpdatedAt == value && conversation.Id.CompareTo(id) < 0)));
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        var (viewed, since) = await ForViewerAsync(conversations, query, cancellationToken);
        conversations = viewed;
        var rows = await conversations
            .OrderByDescending(conversation => conversation.Pinned)
            .ThenByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(limit + 1)
            .Select(conversation => new
            {
                conversation.Id,
                conversation.Title,
                TurnCount = conversation.Turns.Count,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.Channel,
                conversation.Status,
                conversation.AgentId,
                conversation.WidgetId,
                conversation.Pinned,
                conversation.CreatedById,
                conversation.CreatedByName,
                conversation.AssigneeId,
                conversation.AssigneeName,
                conversation.ActiveAgentId,
                conversation.ActiveAgentName,
                Contact = db.Contacts
                    .Where(contact => contact.Id == conversation.ContactId)
                    .Select(contact => new ContactRow(contact.Id, contact.Name, contact.Email, contact.ExternalId))
                    .FirstOrDefault(),
                Preview = conversation.Messages
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Text.Length > PreviewLength ? message.Text.Substring(0, PreviewLength) : message.Text)
                    .FirstOrDefault(),
                Handover = db.Handovers
                    .Where(handover => handover.ConversationId == conversation.Id)
                    .OrderByDescending(handover => handover.RequestedAt)
                    .Select(handover => (HandoverStatus?)handover.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var page = rows.Take(limit).ToList();
        var labels = await ConversationLabelQueries.LoadAsync(db, page.Select(row => row.Id).ToList(), cancellationToken);
        var unreadIds = await UnreadAmongAsync(page.Select(row => row.Id).ToList(), query.ViewerId, since, cancellationToken);
        var next = rows.Count > limit ? new ConversationCursor(page[^1].UpdatedAt, page[^1].Id, page[^1].Pinned).Encode() : null;
        return new ConversationListPageDto(
            page.Select(row => new ConversationSummaryDto(
                    row.Id,
                    row.Title,
                    row.TurnCount,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.Channel.ToString().ToLowerInvariant(),
                    ConversationStatuses.ToText(row.Status),
                    row.AgentId,
                    row.WidgetId,
                    row.Preview,
                    row.Handover?.ToString().ToLowerInvariant())
                {
                    Pinned = row.Pinned,
                    Labels = labels.TryGetValue(row.Id, out var found) ? found : [],
                    Contact = row.Contact?.ToDto(),
                    CreatedBy = CreatorOf(row.CreatedById, row.CreatedByName),
                    Assignee = ConversationAssigneeDto.Of(row.AssigneeId, row.AssigneeName),
                    Unread = unreadIds.Contains(row.Id),
                    ActiveAgent = ConversationActiveAgentDto.Of(row.ActiveAgentId, row.ActiveAgentName),
                })
                .ToList(),
            next);
    }

    // Must run inside the gate.
    private async Task<(IQueryable<Conversation> Conversations, DateTimeOffset? Since)> ForViewerAsync(
        IQueryable<Conversation> conversations,
        ConversationSearchQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? since = query.ViewerId is { } viewerId ? await UnreadQueries.SinceAsync(db, viewerId, cancellationToken) : null;
        if (query.Unread is { } unread)
        {
            conversations = (query.ViewerId, since) is ({ } viewer, { } from)
                ? unread ? conversations.Where(UnreadQueries.Unread(db, viewer, from)) : conversations.Where(Not(UnreadQueries.Unread(db, viewer, from)))
                : unread ? conversations.Where(conversation => false) : conversations;
        }

        return (conversations, since);
    }

    private async Task<HashSet<Guid>> UnreadAmongAsync(List<Guid> ids, string? viewerId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        if (ids.Count == 0 || viewerId is null || since is not { } from)
        {
            return [];
        }

        var unread = await db.Conversations.AsNoTracking()
            .Where(conversation => ids.Contains(conversation.Id))
            .Where(UnreadQueries.Unread(db, viewerId, from))
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);
        return [.. unread];
    }

    private static Expression<Func<Conversation, bool>> Not(Expression<Func<Conversation, bool>> predicate) =>
        Expression.Lambda<Func<Conversation, bool>>(Expression.Not(predicate.Body), predicate.Parameters);

    public async Task<ConversationHistoryDto?> GetHistoryAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (!await db.Conversations.AsNoTracking().AnyAsync(conversation => conversation.Id == conversationId, cancellationToken))
        {
            return null;
        }

        var feedback = await db.Feedback.AsNoTracking()
            .Where(item => item.ConversationId == conversationId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
        var handovers = await db.Handovers.AsNoTracking()
            .Where(handover => handover.ConversationId == conversationId)
            .OrderByDescending(handover => handover.RequestedAt)
            .ToListAsync(cancellationToken);

        var labels = await ConversationLabelQueries.LoadAsync(db, [conversationId], cancellationToken);
        return new ConversationHistoryDto(
            feedback.Select(item => new ConversationFeedbackItemDto(
                    item.Id,
                    item.MessageId,
                    item.Kind.ToString().ToLowerInvariant(),
                    item.Rating,
                    item.Comment,
                    item.Reasons,
                    item.Author.ToString().ToLowerInvariant(),
                    item.CreatedAt))
                .ToList(),
            handovers.Select(handover => new HandoverDto(
                    handover.Id,
                    handover.ConversationId,
                    handover.Channel,
                    handover.Status.ToString().ToLowerInvariant(),
                    handover.Source.ToString().ToLowerInvariant(),
                    handover.Reason,
                    handover.Summary,
                    handover.Assignee,
                    handover.RequestedAt,
                    handover.AcceptedAt,
                    handover.ResolvedAt,
                    handover.LastError))
                .ToList())
        {
            Labels = labels.TryGetValue(conversationId, out var found) ? found : [],
        };
    }

    private async Task<Dictionary<Guid, string>> FirstMatchesAsync(List<Guid> ids, string term, bool npgsql, CancellationToken cancellationToken)
    {
        var messages = db.ConversationMessages.AsNoTracking().Where(message => ids.Contains(message.ConversationId));
        if (npgsql)
        {
            var pattern = "%" + LikeSearch.Escape(term) + "%";
            messages = messages.Where(message => EF.Functions.ILike(message.Text, pattern, LikeSearch.EscapeCharacter));
        }
        else
        {
            var lowered = term.ToLowerInvariant();
            messages = messages.Where(message => message.Text.ToLower().Contains(lowered));
        }

        // At most a page of conversations; picking the first per conversation here works the same in both stores.
        var found = await messages
            .OrderBy(message => message.Sequence)
            .Select(message => new { message.ConversationId, message.Text })
            .ToListAsync(cancellationToken);
        return found.DistinctBy(item => item.ConversationId).ToDictionary(item => item.ConversationId, item => item.Text);
    }

    private async Task<IQueryable<Conversation>> InSegmentAsync(IQueryable<Conversation> conversations, ConversationSearchQuery query, CancellationToken cancellationToken) =>
        query.ContactConditions is { } conditions
            ? await people.WithContactsMatchingAsync(conversations, conditions, query.Now, cancellationToken)
            : conversations;

    private static IQueryable<Conversation> WithoutTests(IQueryable<Conversation> conversations, ConversationSearchQuery query) =>
        query.IncludeTests || query.Channel is not null
            ? conversations
            : conversations.Where(conversation => conversation.Channel != ConversationChannel.Portal);

    private IQueryable<Conversation> Matching(IQueryable<Conversation> conversations, ConversationSearchQuery query, IReadOnlyList<string> terms, bool npgsql)
    {
        if (query.Id is { } id)
        {
            conversations = conversations.Where(conversation => conversation.Id == id);
        }

        if (query.Channel is { } channel)
        {
            conversations = conversations.Where(conversation => conversation.Channel == channel);
        }

        if (query.Pinned is { } pinned)
        {
            conversations = conversations.Where(conversation => conversation.Pinned == pinned);
        }

        if (query.LabelIds.Count > 0)
        {
            var labelIds = query.LabelIds.ToList();
            conversations = conversations.Where(conversation => db.ConversationLabels.Any(item =>
                item.ConversationId == conversation.Id && !item.Removed && labelIds.Contains(item.LabelId)));
        }

        if (query.WidgetId is { } widgetId)
        {
            conversations = conversations.Where(conversation => conversation.WidgetId == widgetId);
        }

        if (query.Statuses.Count > 0)
        {
            var statuses = query.Statuses.ToList();
            conversations = conversations.Where(conversation => statuses.Contains(conversation.Status));
        }

        if (query.AssigneeId is { } assigneeId)
        {
            conversations = conversations.Where(conversation => conversation.AssigneeId == assigneeId);
        }

        if (query.Unassigned)
        {
            conversations = conversations.Where(conversation => conversation.AssigneeName == null);
        }

        if (query.ContactId is { } contactId)
        {
            conversations = conversations.Where(conversation => conversation.ContactId == contactId);
        }

        if (query.CreatedBy is { } createdBy)
        {
            conversations = conversations.Where(conversation => conversation.CreatedById == createdBy);
        }

        if (query.Status is { } status)
        {
            conversations = conversations.Where(conversation => conversation.Status == status);
        }

        if (query.HasHandover is { } hasHandover)
        {
            conversations = hasHandover
                ? conversations.Where(conversation => db.Handovers.Any(handover => handover.ConversationId == conversation.Id))
                : conversations.Where(conversation => !db.Handovers.Any(handover => handover.ConversationId == conversation.Id));
        }

        if (query.MinRating is not null || query.MaxRating is not null)
        {
            var min = query.MinRating ?? 1;
            var max = query.MaxRating ?? 5;
            conversations = conversations.Where(conversation => db.Feedback.Any(item =>
                item.ConversationId == conversation.Id && item.Kind == FeedbackKind.Conversation && item.Rating >= min && item.Rating <= max));
        }

        if (query.ThumbsDown)
        {
            conversations = conversations.Where(conversation => db.Feedback.Any(item =>
                item.ConversationId == conversation.Id && item.Kind == FeedbackKind.Answer && item.Rating < 0));
        }

        if (query.From is { } from)
        {
            // UpdatedAt is never before CreatedAt, so its index narrows the scan to conversations active since then.
            conversations = conversations.Where(conversation => conversation.CreatedAt >= from && conversation.UpdatedAt >= from);
        }

        if (query.To is { } to)
        {
            conversations = conversations.Where(conversation => conversation.CreatedAt < to);
        }

        foreach (var term in terms)
        {
            if (npgsql)
            {
                // One IN (... UNION ALL ...) so each side is read through its trigram index; an OR of the title and a
                // subquery would make PostgreSQL scan every conversation.
                var pattern = "%" + LikeSearch.Escape(term) + "%";
                var matching = db.Conversations
                    .Where(conversation => EF.Functions.ILike(conversation.Title, pattern, LikeSearch.EscapeCharacter))
                    .Select(conversation => conversation.Id)
                    .Concat(db.ConversationMessages
                        .Where(message => EF.Functions.ILike(message.Text, pattern, LikeSearch.EscapeCharacter))
                        .Select(message => message.ConversationId));
                conversations = conversations.Where(conversation => matching.Contains(conversation.Id));
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                conversations = conversations.Where(conversation =>
                    conversation.Title.ToLower().Contains(lowered)
                    || db.ConversationMessages.Any(message => message.ConversationId == conversation.Id && message.Text.ToLower().Contains(lowered)));
            }
        }

        return conversations;
    }

    private static IQueryable<Conversation> After(IQueryable<Conversation> conversations, ConversationSearchQuery query)
    {
        if (query.After is not { } after)
        {
            return conversations;
        }

        var (value, id) = (after.Value, after.Id);
        return (query.Sort, query.Descending) switch
        {
            (ConversationSortKey.Created, true) => conversations.Where(conversation =>
                conversation.CreatedAt < value || (conversation.CreatedAt == value && conversation.Id.CompareTo(id) < 0)),
            (ConversationSortKey.Created, false) => conversations.Where(conversation =>
                conversation.CreatedAt > value || (conversation.CreatedAt == value && conversation.Id.CompareTo(id) > 0)),
            (_, true) => conversations.Where(conversation =>
                conversation.UpdatedAt < value || (conversation.UpdatedAt == value && conversation.Id.CompareTo(id) < 0)),
            _ => conversations.Where(conversation =>
                conversation.UpdatedAt > value || (conversation.UpdatedAt == value && conversation.Id.CompareTo(id) > 0)),
        };
    }

    private static IOrderedQueryable<Conversation> Ordered(IQueryable<Conversation> conversations, ConversationSearchQuery query) =>
        (query.Sort, query.Descending) switch
        {
            (ConversationSortKey.Created, true) => conversations.OrderByDescending(conversation => conversation.CreatedAt).ThenByDescending(conversation => conversation.Id),
            (ConversationSortKey.Created, false) => conversations.OrderBy(conversation => conversation.CreatedAt).ThenBy(conversation => conversation.Id),
            (_, true) => conversations.OrderByDescending(conversation => conversation.UpdatedAt).ThenByDescending(conversation => conversation.Id),
            _ => conversations.OrderBy(conversation => conversation.UpdatedAt).ThenBy(conversation => conversation.Id),
        };

    private static ConversationCreatorDto? CreatorOf(string? id, string? name) => id is null ? null : new ConversationCreatorDto(id, name ?? id);

    private sealed record ContactRow(Guid Id, string? Name, string? Email, string? ExternalId)
    {
        public ConversationContactDto ToDto() => new(Id, Contact.DisplayNameOf(Id, Name), Email, ExternalId is not null);
    }

    internal static string? Passage(string? text, string term)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= MatchLength)
        {
            return flat;
        }

        var at = Math.Max(0, flat.IndexOf(term, StringComparison.OrdinalIgnoreCase));
        var start = Math.Max(0, at - (MatchLength / 3));
        var end = Math.Min(flat.Length, start + MatchLength);
        start = Math.Max(0, end - MatchLength);
        if (start > 0 && flat.IndexOf(' ', start) is var space and >= 0 && space < at)
        {
            start = space + 1;
        }

        if (end < flat.Length && flat.LastIndexOf(' ', end - 1) is var last && last > Math.Max(start, at))
        {
            end = last;
        }

        return (start > 0 ? "…" : string.Empty) + flat[start..end] + (end < flat.Length ? "…" : string.Empty);
    }
}
