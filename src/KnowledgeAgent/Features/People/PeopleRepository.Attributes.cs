using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.People;

internal sealed partial class PeopleRepository
{
    private const int WriteAttempts = 3;

    public async Task RecordVisitAsync(Guid contactId, VisitorVisit visit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (await db.Contacts.FirstOrDefaultAsync(contact => contact.Id == contactId, cancellationToken) is not { } contact)
            {
                return;
            }

            if (visit.NewSession && now - contact.LastSeenAt >= VisitorSessions.IdleTime)
            {
                contact.SessionCount++;
            }

            contact.LastPageUrl = visit.PageUrl ?? contact.LastPageUrl;
            contact.LastReferrer = visit.PageUrl is null ? contact.LastReferrer : visit.Referrer;
            contact.Language = visit.Language ?? contact.Language;
            contact.LastSeenAt = Later(contact.LastSeenAt, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<AttributeChangeResult> SetVisitorAttributesAsync(
        Guid visitorId,
        IReadOnlyDictionary<string, JsonElement> attributes,
        string source,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count == 0)
        {
            return AttributeChangeResult.None;
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        return await WithRetryAsync(async () =>
        {
            var visitor = await db.WidgetVisitors.FirstAsync(item => item.Id == visitorId, cancellationToken);
            var hadContact = visitor.ContactId is not null && await db.Contacts.AnyAsync(item => item.Id == visitor.ContactId, cancellationToken);
            var contact = await ContactOfAsync(visitor, now, cancellationToken);
            var result = await MergeAsync(contact, attributes, source, now, cancellationToken);
            if (!hadContact && contact.Attributes.Count == 0)
            {
                // Nothing was stored, so the visitor stays without a contact.
                db.ChangeTracker.Clear();
                return result;
            }

            contact.LastSeenAt = Later(contact.LastSeenAt, now);
            await db.SaveChangesAsync(cancellationToken);
            return result;
        });
    }

    public async Task<AttributeChangeResult?> SetAttributesAsync(
        Guid contactId,
        IReadOnlyDictionary<string, JsonElement> attributes,
        string source,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        using var _ = await gate.EnterAsync(cancellationToken);
        return await WithRetryAsync(async () =>
        {
            if (await db.Contacts.FirstOrDefaultAsync(item => item.Id == contactId, cancellationToken) is not { } contact)
            {
                return null;
            }

            var result = await MergeAsync(contact, attributes, source, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return result;
        });
    }

    public async Task<ServerContactResult> UpsertFromServerAsync(
        string userId,
        string? name,
        string? email,
        IReadOnlyDictionary<string, JsonElement> attributes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        using var _ = await gate.EnterAsync(cancellationToken);
        return await WithRetryAsync(async () =>
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(item => item.ExternalId == userId, cancellationToken);
            var created = contact is null;
            if (contact is null)
            {
                contact = new Contact { Id = Guid.CreateVersion7(), ExternalId = userId, FirstSeenAt = now, LastSeenAt = now };
                db.Contacts.Add(contact);
            }

            contact.Name = name ?? contact.Name;
            contact.Email = email ?? contact.Email;
            var result = await MergeAsync(contact, attributes, AttributeSources.Server, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new ServerContactResult(contact.Id, created, result);
        });
    }

    public async Task<IReadOnlyDictionary<string, ContactAttributeDefinition>> AttributeRegistryAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.ContactAttributes.AsNoTracking().ToDictionaryAsync(definition => definition.Key, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<IReadOnlyList<ContactAttributeDto>> ListAttributesAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var definitions = await db.ContactAttributes.AsNoTracking().ToListAsync(cancellationToken);
        var counts = await CountsByKeyAsync(cancellationToken);
        return definitions
            .OrderBy(definition => definition.Label, StringComparer.OrdinalIgnoreCase)
            .Select(definition => new ContactAttributeDto(
                definition.Key,
                definition.Label,
                definition.Type,
                definition.FirstSeenAt,
                definition.LastSeenAt,
                counts.GetValueOrDefault(definition.Key),
                definition.Visible,
                definition.ExposeToAgent,
                definition.Trust))
            .ToList();
    }

    public async Task<ContactAttributeDefinition?> UpdateAttributeAsync(string key, UpdateContactAttributeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (await db.ContactAttributes.FirstOrDefaultAsync(definition => definition.Key == key, cancellationToken) is not { } definition)
            {
                return null;
            }

            definition.Label = request.Label?.Trim() is { Length: > 0 } label ? label : definition.Label;
            definition.Visible = request.Visible ?? definition.Visible;
            definition.ExposeToAgent = request.ExposeToAgent ?? definition.ExposeToAgent;
            definition.Trust = request.Trust ?? definition.Trust;
            await db.SaveChangesAsync(cancellationToken);
            return definition;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<int?> DeleteAttributeAsync(string key, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            if (!await db.ContactAttributes.AnyAsync(definition => definition.Key == key, cancellationToken))
            {
                return null;
            }

            int contacts;
            if (db.Database.IsNpgsql())
            {
                // One transaction, retried as a whole, so the key never stays on some contacts without its registry row.
                return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    var changed = await db.Database.ExecuteSqlRawAsync(
                        "UPDATE contacts SET attributes = attributes - {0}, attribute_sources = attribute_sources - {0} WHERE jsonb_exists(attributes, {0})",
                        [key],
                        cancellationToken);
                    await db.ContactAttributes.Where(definition => definition.Key == key).ExecuteDeleteAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return changed;
                });
            }

            var having = (await db.Contacts.ToListAsync(cancellationToken)).Where(contact => contact.Attributes.ContainsKey(key)).ToList();
            foreach (var contact in having)
            {
                contact.Attributes = contact.Attributes.Where(entry => entry.Key != key).ToDictionary(StringComparer.Ordinal);
                contact.AttributeSources = contact.AttributeSources.Where(entry => entry.Key != key).ToDictionary(StringComparer.Ordinal);
            }

            db.ContactAttributes.Remove(await db.ContactAttributes.FirstAsync(definition => definition.Key == key, cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            contacts = having.Count;
            return contacts;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<AttributeValueCountDto>> AttributeValuesAsync(string key, int limit, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            return await db.Database
                .SqlQueryRaw<AttributeValueCountDto>(
                    """
                    SELECT attributes ->> {0} AS "Value", count(*)::int AS "Contacts" FROM contacts
                    WHERE jsonb_typeof(attributes -> {0}) = 'string'
                    GROUP BY 1 ORDER BY 2 DESC, 1 LIMIT {1}
                    """,
                    key,
                    limit)
                .ToListAsync(cancellationToken);
        }

        return (await db.Contacts.AsNoTracking().Select(contact => contact.Attributes).ToListAsync(cancellationToken))
            .Where(values => values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            .GroupBy(values => values[key].GetString()!, StringComparer.Ordinal)
            .Select(group => new AttributeValueCountDto(group.Key, group.Count()))
            .OrderByDescending(item => item.Contacts)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    public async Task<IQueryable<Conversation>> WithContactsMatchingAsync(
        IQueryable<Conversation> conversations,
        IReadOnlyList<ResolvedCondition> conditions,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        if (db.Database.IsNpgsql())
        {
            var matching = ContactsMatching(conditions, now);
            return conversations.Where(conversation => matching.Any(contact => contact.Id == conversation.ContactId));
        }

        List<Guid> ids;
        using (await gate.EnterAsync(cancellationToken))
        {
            ids = (await MatchInMemoryAsync(await db.Contacts.AsNoTracking().ToListAsync(cancellationToken), conditions, now, int.MaxValue, cancellationToken))
                .Select(contact => contact.Id)
                .ToList();
        }

        return conversations.Where(conversation => conversation.ContactId != null && ids.Contains(conversation.ContactId.Value));
    }

    public async Task<IReadOnlySet<Guid>> MatchingAmongAsync(
        IReadOnlyCollection<Guid> contactIds,
        IReadOnlyList<ResolvedCondition> conditions,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contactIds);
        if (contactIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = contactIds.ToList();
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            return (await ContactsMatching(conditions, now).Where(contact => ids.Contains(contact.Id)).Select(contact => contact.Id).ToListAsync(cancellationToken)).ToHashSet();
        }

        var contacts = await db.Contacts.AsNoTracking().Where(contact => ids.Contains(contact.Id)).ToListAsync(cancellationToken);
        return (await MatchInMemoryAsync(contacts, conditions, now, int.MaxValue, cancellationToken)).Select(contact => contact.Id).ToHashSet();
    }

    public async Task<IReadOnlyList<VisitorFact>> AgentFactsAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var exposed = await db.ContactAttributes.AsNoTracking().Where(definition => definition.ExposeToAgent).ToListAsync(cancellationToken);
        if (exposed.Count == 0 || await db.Contacts.AsNoTracking().FirstOrDefaultAsync(contact => contact.Id == contactId, cancellationToken) is not { } contact)
        {
            return [];
        }

        return exposed
            .Where(definition => contact.Attributes.ContainsKey(definition.Key))
            .OrderBy(definition => definition.Label, StringComparer.OrdinalIgnoreCase)
            .Select(definition =>
            {
                var value = contact.Attributes[definition.Key];
                var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
                var source = contact.AttributeSources.GetValueOrDefault(definition.Key)?.Source ?? AttributeSources.Verified;
                return new VisitorFact(definition.Label, text, source != AttributeSources.Browser);
            })
            .ToList();
    }

    private async Task<Dictionary<string, ContactAttributeDefinition>> TrackedRegistryAsync(CancellationToken cancellationToken) =>
        await db.ContactAttributes.ToDictionaryAsync(definition => definition.Key, StringComparer.Ordinal, cancellationToken);

    private async Task<AttributeChangeResult> MergeAsync(Contact contact, IReadOnlyDictionary<string, JsonElement> attributes, string source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var registry = await TrackedRegistryAsync(cancellationToken);
        var added = new List<ContactAttributeDefinition>();
        var result = AttributeValues.Merge(contact, attributes, source, now, registry, added);
        db.ContactAttributes.AddRange(added);
        return result;
    }

    // A concurrent write may add the same new key first; the next attempt sees it.
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (DbUpdateException) when (attempt < WriteAttempts)
            {
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<Dictionary<string, int>> CountsByKeyAsync(CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            return await db.Database
                .SqlQueryRaw<KeyCount>("SELECT key AS \"Key\", count(*)::int AS \"Count\" FROM contacts, jsonb_object_keys(attributes) AS key GROUP BY key")
                .ToDictionaryAsync(row => row.Key, row => row.Count, StringComparer.Ordinal, cancellationToken);
        }

        return (await db.Contacts.AsNoTracking().Select(contact => contact.Attributes).ToListAsync(cancellationToken))
            .SelectMany(values => values.Keys)
            .GroupBy(key => key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private IQueryable<Contact> ContactsMatching(IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now)
    {
        var (where, parameters) = ContactFilters.ToSql(conditions, now);
#pragma warning disable EF1002 // The clause is built from checked keys and operators; every value is a parameter.
        return db.Contacts.FromSqlRaw($"SELECT * FROM contacts WHERE {where}", parameters.Cast<object>().ToArray()).AsNoTracking();
#pragma warning restore EF1002
    }

    private async Task<List<Contact>> MatchInMemoryAsync(List<Contact> contacts, IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        if (conditions.Count == 0)
        {
            return contacts.Take(take).ToList();
        }

        ILookup<Guid, Guid> widgets = Array.Empty<(Guid, Guid)>().ToLookup(pair => pair.Item1, pair => pair.Item2);
        if (conditions.Any(condition => condition is { BuiltIn: true, Attr: "widget" }))
        {
            widgets = (await db.WidgetVisitors.AsNoTracking().Where(visitor => visitor.ContactId != null)
                    .Select(visitor => new { ContactId = visitor.ContactId!.Value, visitor.WidgetId })
                    .ToListAsync(cancellationToken))
                .ToLookup(row => row.ContactId, row => row.WidgetId);
        }

        return contacts
            .Where(contact => ContactFilters.Matches(contact, conditions, now, id => widgets[id].ToHashSet()))
            .Take(take)
            .ToList();
    }

    private sealed record KeyCount(string Key, int Count);
}
