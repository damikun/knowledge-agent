using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.People;

public interface IPeopleRepository
{
    Task<PeoplePageDto> ListAsync(PeopleQuery query, CancellationToken cancellationToken = default);

    Task<int> CountAsync(PeopleQuery query, CancellationToken cancellationToken = default);
    Task<Contact?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PersonDto?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpdateAsync(Contact contact, CancellationToken cancellationToken = default);

    Task<ErasePersonResultDto?> EraseAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PersonExportDto?> ExportAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<Guid> EnsureForVisitorAsync(Guid visitorId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task SaveVisitorDetailsAsync(Guid visitorId, string? name, string? email, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IdentifiedContact> IdentifyAsync(Guid visitorId, VerifiedUser user, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task RecordVisitAsync(Guid contactId, VisitorVisit visit, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<AttributeChangeResult> SetVisitorAttributesAsync(Guid visitorId, IReadOnlyDictionary<string, JsonElement> attributes, string source, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<AttributeChangeResult?> SetAttributesAsync(Guid contactId, IReadOnlyDictionary<string, JsonElement> attributes, string source, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<ServerContactResult> UpsertFromServerAsync(string userId, string? name, string? email, IReadOnlyDictionary<string, JsonElement> attributes, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, ContactAttributeDefinition>> AttributeRegistryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactAttributeDto>> ListAttributesAsync(CancellationToken cancellationToken = default);

    Task<ContactAttributeDefinition?> UpdateAttributeAsync(string key, UpdateContactAttributeRequest request, CancellationToken cancellationToken = default);

    Task<int?> DeleteAttributeAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeValueCountDto>> AttributeValuesAsync(string key, int limit, CancellationToken cancellationToken = default);

    Task<IQueryable<Conversation>> WithContactsMatchingAsync(IQueryable<Conversation> conversations, IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> MatchingAmongAsync(IReadOnlyCollection<Guid> contactIds, IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VisitorFact>> AgentFactsAsync(Guid contactId, CancellationToken cancellationToken = default);

    Task<int> TidyAsync(CancellationToken cancellationToken = default);
}

internal sealed partial class PeopleRepository(KnowledgeDbContext db, DbContextGate gate) : IPeopleRepository
{
    private const int IdentifyAttempts = 3;

    // An empty anonymous contact this old is a page open nothing came of; younger ones may be about to get a conversation.
    private static readonly TimeSpan EmptyContactGrace = TimeSpan.FromDays(1);

    public async Task<PeoplePageDto> ListAsync(PeopleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, PeopleQuery.MaxLimit);
        var contacts = Filtered(query);
        if (query.After is { } after)
        {
            var (value, id) = (after.Value, after.Id);
            contacts = contacts.Where(contact => contact.LastSeenAt < value || (contact.LastSeenAt == value && contact.Id.CompareTo(id) < 0));
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        var ordered = contacts.OrderByDescending(contact => contact.LastSeenAt).ThenByDescending(contact => contact.Id);
        var rows = query.Conditions.Count == 0 || db.Database.IsNpgsql()
            ? await ordered.Take(limit + 1).ToListAsync(cancellationToken)
            : await MatchInMemoryAsync(await ordered.ToListAsync(cancellationToken), query.Conditions, query.Now, limit + 1, cancellationToken);
        var page = rows.Take(limit).ToList();
        var widgets = await WidgetsOfAsync(page.Select(contact => contact.Id).ToList(), cancellationToken);
        var next = rows.Count > limit ? new ConversationCursor(page[^1].LastSeenAt, page[^1].Id).Encode() : null;
        return new PeoplePageDto(
            page.Select(contact => new PersonSummaryDto(
                    contact.Id,
                    contact.DisplayName,
                    contact.Email,
                    contact.ExternalId,
                    contact.Identified,
                    contact.FirstSeenAt,
                    contact.LastSeenAt,
                    contact.ConversationCount,
                    widgets.TryGetValue(contact.Id, out var used) ? used.Select(widget => new PersonWidgetDto(widget.Id, widget.Name)).ToList() : [])
                {
                    SessionCount = contact.SessionCount,
                    Returning = contact.Returning,
                    Language = contact.Language,
                    LastPageUrl = contact.LastPageUrl,
                    Attributes = contact.Attributes,
                })
                .ToList(),
            next);
    }

    public async Task<int> CountAsync(PeopleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var contacts = Filtered(query);
        using var _ = await gate.EnterAsync(cancellationToken);
        return query.Conditions.Count == 0 || db.Database.IsNpgsql()
            ? await contacts.CountAsync(cancellationToken)
            : (await MatchInMemoryAsync(await contacts.ToListAsync(cancellationToken), query.Conditions, query.Now, int.MaxValue, cancellationToken)).Count;
    }

    // Everything but the attribute conditions on the in-memory store, which are checked over the loaded rows.
    private IQueryable<Contact> Filtered(PeopleQuery query)
    {
        var contacts = query.Conditions.Count > 0 && db.Database.IsNpgsql()
            ? ContactsMatching(query.Conditions, query.Now)
            : db.Contacts.AsNoTracking();
        if (query.Identified is { } identified)
        {
            contacts = identified ? contacts.Where(contact => contact.ExternalId != null) : contacts.Where(contact => contact.ExternalId == null);
        }

        if (query.ActiveOnly)
        {
            contacts = contacts.Where(contact => contact.ConversationCount > 0 || contact.ExternalId != null);
        }

        if (query.WidgetId is { } widgetId)
        {
            contacts = contacts.Where(contact => db.WidgetVisitors.Any(visitor => visitor.ContactId == contact.Id && visitor.WidgetId == widgetId));
        }

        foreach (var term in LikeSearch.Terms(query.Text))
        {
            if (db.Database.IsNpgsql())
            {
                var pattern = "%" + LikeSearch.Escape(term) + "%";
                contacts = contacts.Where(contact => EF.Functions.ILike(contact.Name!, pattern, LikeSearch.EscapeCharacter)
                    || EF.Functions.ILike(contact.Email!, pattern, LikeSearch.EscapeCharacter)
                    || EF.Functions.ILike(contact.ExternalId!, pattern, LikeSearch.EscapeCharacter));
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                contacts = contacts.Where(contact => (contact.Name != null && contact.Name.ToLower().Contains(lowered))
                    || (contact.Email != null && contact.Email.ToLower().Contains(lowered))
                    || (contact.ExternalId != null && contact.ExternalId.ToLower().Contains(lowered)));
            }
        }

        return contacts;
    }

    public async Task<Contact?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Contacts.AsNoTracking().FirstOrDefaultAsync(contact => contact.Id == id, cancellationToken);
    }

    public async Task<PersonDto?> GetProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await ProfileAsync(id, cancellationToken);
    }

    public async Task UpdateAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.Contacts.Update(contact);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<ErasePersonResultDto?> EraseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (contact is null)
            {
                return null;
            }

            var visitors = await db.WidgetVisitors.Where(visitor => visitor.ContactId == id).ToListAsync(cancellationToken);
            var visitorIds = visitors.Select(visitor => (Guid?)visitor.Id).ToList();
            var conversationIds = await ConversationsOf(id, visitorIds).Select(conversation => conversation.Id).ToListAsync(cancellationToken);

            if (db.Database.IsRelational())
            {
                // One transaction, retried as a whole, so a failure never leaves half a person behind.
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await db.Conversations.Where(conversation => conversationIds.Contains(conversation.Id)).ExecuteDeleteAsync(cancellationToken);
                    await db.Contacts.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                });
            }
            else
            {
                // The in-memory provider does not cascade, so every dependent goes explicitly.
                db.ConversationLabels.RemoveRange(await db.ConversationLabels.Where(item => conversationIds.Contains(item.ConversationId)).ToListAsync(cancellationToken));
                db.Feedback.RemoveRange(await db.Feedback.Where(item => conversationIds.Contains(item.ConversationId)).ToListAsync(cancellationToken));
                db.Handovers.RemoveRange(await db.Handovers.Where(item => conversationIds.Contains(item.ConversationId)).ToListAsync(cancellationToken));
                db.ConversationTurns.RemoveRange(await db.ConversationTurns.Where(item => conversationIds.Contains(item.ConversationId)).ToListAsync(cancellationToken));
                db.ConversationMessages.RemoveRange(await db.ConversationMessages.Where(item => conversationIds.Contains(item.ConversationId)).ToListAsync(cancellationToken));
                db.Conversations.RemoveRange(await db.Conversations.Where(item => conversationIds.Contains(item.Id)).ToListAsync(cancellationToken));
                db.WidgetVisitors.RemoveRange(visitors);
                db.Contacts.Remove(contact);
                await db.SaveChangesAsync(cancellationToken);
            }

            return new ErasePersonResultDto(conversationIds.Count, visitors.Count);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<PersonExportDto?> ExportAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (await ProfileAsync(id, cancellationToken) is not { } person)
        {
            return null;
        }

        var visitors = await db.WidgetVisitors.AsNoTracking().Where(visitor => visitor.ContactId == id).OrderBy(visitor => visitor.CreatedAt).ToListAsync(cancellationToken);
        var visitorIds = visitors.Select(visitor => (Guid?)visitor.Id).ToList();
        var conversations = await ConversationsOf(id, visitorIds).AsNoTracking().OrderBy(conversation => conversation.CreatedAt).ToListAsync(cancellationToken);
        var ids = conversations.Select(conversation => conversation.Id).ToList();
        var messages = (await db.ConversationMessages.AsNoTracking().Where(message => ids.Contains(message.ConversationId)).ToListAsync(cancellationToken))
            .ToLookup(message => message.ConversationId);
        var feedback = (await db.Feedback.AsNoTracking().Where(item => ids.Contains(item.ConversationId)).ToListAsync(cancellationToken))
            .ToLookup(item => item.ConversationId);
        var handovers = (await db.Handovers.AsNoTracking().Where(item => ids.Contains(item.ConversationId)).ToListAsync(cancellationToken))
            .ToLookup(item => item.ConversationId);

        return new PersonExportDto(
            now,
            person,
            visitors.Select(visitor => new PersonExportVisitorDto(visitor.Id, visitor.WidgetId, visitor.Name, visitor.Email, visitor.CreatedAt, visitor.LastSeenAt)).ToList(),
            conversations.Select(conversation => new PersonExportConversationDto(
                    conversation.Id,
                    conversation.Title,
                    conversation.Channel.ToString().ToLowerInvariant(),
                    ConversationStatuses.ToText(conversation.Status),
                    conversation.WidgetId,
                    conversation.CreatedAt,
                    conversation.UpdatedAt,
                    messages[conversation.Id].OrderBy(message => message.Sequence)
                        .Select(message => new PersonExportMessageDto(message.Sequence, message.Author.ToString().ToLowerInvariant(), message.AuthorName, message.Text, message.CreatedAt))
                        .ToList(),
                    feedback[conversation.Id].OrderBy(item => item.CreatedAt)
                        .Select(item => new PersonExportFeedbackDto(item.Kind.ToString().ToLowerInvariant(), item.Rating, item.Comment, item.Reasons, item.Author.ToString().ToLowerInvariant(), item.CreatedAt))
                        .ToList(),
                    handovers[conversation.Id].OrderBy(item => item.RequestedAt)
                        .Select(item => new PersonExportHandoverDto(item.Channel, item.Status.ToString().ToLowerInvariant(), item.Reason, item.Summary, item.RequestedAt, item.ResolvedAt))
                        .ToList()))
                .ToList());
    }

    public async Task<Guid> EnsureForVisitorAsync(Guid visitorId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var visitor = await db.WidgetVisitors.FirstAsync(item => item.Id == visitorId, cancellationToken);
            var contact = await ContactOfAsync(visitor, now, cancellationToken);
            contact.LastSeenAt = Later(contact.LastSeenAt, now);
            await db.SaveChangesAsync(cancellationToken);
            return contact.Id;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task SaveVisitorDetailsAsync(Guid visitorId, string? name, string? email, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var visitor = await db.WidgetVisitors.FirstAsync(item => item.Id == visitorId, cancellationToken);
            var contact = await ContactOfAsync(visitor, now, cancellationToken);
            if (!string.IsNullOrWhiteSpace(name) && (!contact.Identified || contact.Name is null))
            {
                contact.Name = name;
            }

            if (!string.IsNullOrWhiteSpace(email) && (!contact.Identified || contact.Email is null))
            {
                contact.Email = email;
            }

            contact.LastSeenAt = Later(contact.LastSeenAt, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IdentifiedContact> IdentifyAsync(Guid visitorId, VerifiedUser user, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        using var _ = await gate.EnterAsync(cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await IdentifyOnceAsync(visitorId, user, now, cancellationToken);
            }
            catch (DbUpdateException) when (attempt < IdentifyAttempts)
            {
                // Another session created the contact with this user id (or a new attribute key) at the same time; the next attempt finds it.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<int> TidyAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var quietSince = DateTimeOffset.UtcNow - EmptyContactGrace;
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE contacts SET conversation_count = counted.count
                FROM (SELECT c.id, (SELECT count(*) FROM conversations WHERE contact_id = c.id)::int AS count FROM contacts c) AS counted
                WHERE contacts.id = counted.id AND contacts.conversation_count <> counted.count
                """,
                cancellationToken);

            var empty = await db.Contacts
                .Where(contact => contact.ExternalId == null && contact.Name == null && contact.Email == null && contact.Notes == null
                    && contact.LastSeenAt < quietSince
                    && !db.Conversations.Any(conversation => conversation.ContactId == contact.Id))
                .Select(contact => new { contact.Id, contact.Attributes })
                .ToListAsync(cancellationToken);
            var emptyIds = empty.Where(item => item.Attributes.Count == 0).Select(item => item.Id).ToList();
            if (emptyIds.Count > 0)
            {
                await db.WidgetVisitors.Where(visitor => visitor.ContactId != null && emptyIds.Contains(visitor.ContactId.Value))
                    .ExecuteUpdateAsync(update => update.SetProperty(visitor => visitor.ContactId, (Guid?)null), cancellationToken);
            }

            return await db.Contacts
                .Where(contact => contact.ExternalId == null
                    && !db.WidgetVisitors.Any(visitor => visitor.ContactId == contact.Id)
                    && !db.Conversations.Any(conversation => conversation.ContactId == contact.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        try
        {
            var contacts = await db.Contacts.ToListAsync(cancellationToken);
            var counts = await db.Conversations.Where(conversation => conversation.ContactId != null)
                .GroupBy(conversation => conversation.ContactId!.Value)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);
            var visitors = await db.WidgetVisitors.Where(visitor => visitor.ContactId != null).ToListAsync(cancellationToken);
            var removed = 0;
            foreach (var contact in contacts)
            {
                contact.ConversationCount = counts.GetValueOrDefault(contact.Id);
                if (contact is { Identified: false, ConversationCount: 0, Name: null, Email: null, Notes: null, Attributes.Count: 0 } && contact.LastSeenAt < quietSince)
                {
                    foreach (var visitor in visitors.Where(visitor => visitor.ContactId == contact.Id))
                    {
                        visitor.ContactId = null;
                    }
                }
            }

            var linked = visitors.Where(visitor => visitor.ContactId != null).Select(visitor => visitor.ContactId!.Value).ToHashSet();
            foreach (var contact in contacts)
            {
                if (!contact.Identified && contact.ConversationCount == 0 && !linked.Contains(contact.Id))
                {
                    db.Contacts.Remove(contact);
                    removed++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return removed;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<IdentifiedContact> IdentifyOnceAsync(Guid visitorId, VerifiedUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var visitor = await db.WidgetVisitors.FirstAsync(item => item.Id == visitorId, cancellationToken);
        var contact = await db.Contacts.FirstOrDefaultAsync(item => item.ExternalId == user.UserId, cancellationToken);
        if (contact is null)
        {
            contact = new Contact
            {
                Id = Guid.CreateVersion7(),
                ExternalId = user.UserId,
                FirstSeenAt = now,
                LastSeenAt = now,
                SessionCount = visitor.ContactId is null ? Math.Max(visitor.SessionCount, 1) : 0,
            };
            db.Contacts.Add(contact);
        }
        else if (now - contact.LastSeenAt >= VisitorSessions.IdleTime)
        {
            contact.SessionCount++;
        }

        contact.LastPageUrl = visitor.LastPageUrl ?? contact.LastPageUrl;
        contact.LastReferrer = visitor.LastReferrer ?? contact.LastReferrer;
        contact.Language = visitor.Language ?? contact.Language;

        // Linked before the old contact may go, so deleting it never takes this browser with it.
        var previousId = visitor.ContactId;
        visitor.ContactId = contact.Id;
        if (previousId is { } oldId && oldId != contact.Id
            && await db.Contacts.FirstOrDefaultAsync(item => item.Id == oldId, cancellationToken) is { Identified: false } anonymous)
        {
            foreach (var conversation in await db.Conversations.Where(item => item.ContactId == anonymous.Id).ToListAsync(cancellationToken))
            {
                conversation.ContactId = contact.Id;
            }

            contact.FirstSeenAt = anonymous.FirstSeenAt < contact.FirstSeenAt ? anonymous.FirstSeenAt : contact.FirstSeenAt;
            contact.Name ??= anonymous.Name;
            contact.Email ??= anonymous.Email;
            contact.SessionCount += anonymous.SessionCount;

            var values = new Dictionary<string, JsonElement>(contact.Attributes, StringComparer.Ordinal);
            var stamps = new Dictionary<string, AttributeStamp>(contact.AttributeSources, StringComparer.Ordinal);
            foreach (var (key, value) in anonymous.Attributes)
            {
                if (!values.ContainsKey(key) && values.Count < ContactAttributeLimits.MaxKeysPerContact)
                {
                    values[key] = value;
                    stamps[key] = anonymous.AttributeSources.GetValueOrDefault(key) ?? new AttributeStamp(AttributeSources.Browser, now);
                }
            }

            contact.Attributes = values;
            contact.AttributeSources = stamps;
            if (anonymous.Notes is not null)
            {
                contact.Notes = contact.Notes is null ? anonymous.Notes : $"{contact.Notes}\n\n{anonymous.Notes}";
                contact.Notes = contact.Notes[..Math.Min(contact.Notes.Length, Contact.MaxNotesLength)];
            }

            if (!await db.WidgetVisitors.AnyAsync(item => item.ContactId == anonymous.Id && item.Id != visitor.Id, cancellationToken))
            {
                db.Contacts.Remove(anonymous);
            }
        }

        foreach (var conversation in await db.Conversations.Where(item => item.VisitorId == visitor.Id && item.ContactId != contact.Id).ToListAsync(cancellationToken))
        {
            conversation.ContactId = contact.Id;
        }

        contact.Name = user.Name ?? contact.Name;
        contact.Email = user.Email ?? contact.Email;
        var registry = await TrackedRegistryAsync(cancellationToken);
        var added = new List<ContactAttributeDefinition>();
        var attributes = AttributeValues.Merge(contact, user.Attributes, AttributeSources.Verified, now, registry, added);
        db.ContactAttributes.AddRange(added);
        contact.LastSeenAt = Later(contact.LastSeenAt, now);

        visitor.Name = contact.Name ?? visitor.Name;
        visitor.Email = contact.Email ?? visitor.Email;
        await db.SaveChangesAsync(cancellationToken);

        contact.ConversationCount = await db.Conversations.CountAsync(item => item.ContactId == contact.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new IdentifiedContact(contact, attributes);
    }

    private async Task<Contact> ContactOfAsync(WidgetVisitor visitor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (visitor.ContactId is { } contactId && await db.Contacts.FirstOrDefaultAsync(item => item.Id == contactId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var contact = new Contact
        {
            Id = Guid.CreateVersion7(),
            Name = visitor.Name,
            Email = visitor.Email,
            FirstSeenAt = visitor.CreatedAt == default ? now : visitor.CreatedAt,
            LastSeenAt = now,

            SessionCount = Math.Max(visitor.SessionCount, 1),
            LastPageUrl = visitor.LastPageUrl,
            LastReferrer = visitor.LastReferrer,
            Language = visitor.Language,
        };
        db.Contacts.Add(contact);
        visitor.ContactId = contact.Id;
        foreach (var conversation in await db.Conversations.Where(item => item.VisitorId == visitor.Id && item.ContactId == null).ToListAsync(cancellationToken))
        {
            conversation.ContactId = contact.Id;
            contact.ConversationCount++;
        }

        return contact;
    }

    private IQueryable<Conversation> ConversationsOf(Guid contactId, List<Guid?> visitorIds) =>
        db.Conversations.Where(conversation => conversation.ContactId == contactId || visitorIds.Contains(conversation.VisitorId));

    private async Task<PersonDto?> ProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var usage = await db.WidgetVisitors.AsNoTracking()
            .Where(visitor => visitor.ContactId == id)
            .Join(db.Widgets, visitor => visitor.WidgetId, widget => widget.Id, (visitor, widget) => new { widget.Id, widget.Name, visitor.CreatedAt, visitor.LastSeenAt })
            .ToListAsync(cancellationToken);
        var widgets = usage
            .GroupBy(item => item.Id)
            .Select(group => new PersonWidgetUsageDto(group.Key, group.First().Name, group.Min(item => item.CreatedAt), group.Max(item => item.LastSeenAt)))
            .OrderByDescending(item => item.LastSeenAt)
            .ToList();

        var conversations = db.Conversations.Where(conversation => conversation.ContactId == id);
        var conversationCount = await conversations.CountAsync(cancellationToken);
        var handovers = await db.Handovers.CountAsync(handover => conversations.Any(conversation => conversation.Id == handover.ConversationId), cancellationToken);
        var ratings = await db.Feedback
            .Where(item => item.Kind == FeedbackKind.Conversation && conversations.Any(conversation => conversation.Id == item.ConversationId))
            .Select(item => item.Rating)
            .ToListAsync(cancellationToken);

        var registry = await db.ContactAttributes.AsNoTracking().ToDictionaryAsync(definition => definition.Key, cancellationToken);
        var attributes = contact.Attributes
            .Select(entry =>
            {
                var definition = registry.GetValueOrDefault(entry.Key);
                var stamp = contact.AttributeSources.GetValueOrDefault(entry.Key);
                return new PersonAttributeDto(
                    entry.Key,
                    definition?.Label ?? ContactAttributeDefinition.LabelOf(entry.Key),
                    definition?.Type ?? AttributeValues.InferType(entry.Value) ?? AttributeTypes.String,
                    entry.Value,
                    stamp?.Source ?? AttributeSources.Verified,
                    stamp?.UpdatedAt ?? contact.LastSeenAt,
                    definition?.Visible ?? true);
            })
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PersonDto(
            contact.Id,
            contact.DisplayName,
            contact.Name,
            contact.Email,
            contact.ExternalId,
            contact.Identified,
            contact.Notes,
            attributes,
            contact.FirstSeenAt,
            contact.LastSeenAt,
            widgets,
            new PersonStatsDto(conversationCount, handovers, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2), ratings.Count, contact.LastSeenAt))
        {
            SessionCount = contact.SessionCount,
            Returning = contact.Returning,
            LastPageUrl = contact.LastPageUrl,
            LastReferrer = contact.LastReferrer,
            Language = contact.Language,
        };
    }

    private async Task<Dictionary<Guid, List<PersonWidgetDto>>> WidgetsOfAsync(List<Guid> contactIds, CancellationToken cancellationToken)
    {
        var rows = await db.WidgetVisitors.AsNoTracking()
            .Where(visitor => visitor.ContactId != null && contactIds.Contains(visitor.ContactId.Value))
            .Join(db.Widgets, visitor => visitor.WidgetId, widget => widget.Id, (visitor, widget) => new { ContactId = visitor.ContactId!.Value, widget.Id, widget.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.ContactId)
            .ToDictionary(group => group.Key, group => group.DistinctBy(row => row.Id).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Select(row => new PersonWidgetDto(row.Id, row.Name)).ToList());
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
