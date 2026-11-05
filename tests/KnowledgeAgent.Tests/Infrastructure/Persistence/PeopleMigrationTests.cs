using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class PeopleMigrationTests(PostgresPersistenceFixture fixture) : IClassFixture<PostgresPersistenceFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_existing_visitor_becomes_a_person_with_its_details_and_conversations()
    {
        var connectionString = fixture.ConnectionStringFor($"ka_people_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261105112432_AddAlerts", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var agentId = Guid.NewGuid();
        var widgetId = Guid.NewGuid();
        var jane = Guid.NewGuid();
        var anonymous = Guid.NewGuid();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO agents (id, key, name, profile_name, folder_ids, tools, allow_general_knowledge, is_built_in, created_at, updated_at, handover_policy) "
            + "VALUES ($1, 'shop', 'Shop', 'orchestrator', '[]', '[]', false, false, now(), now(), 'OnRequest')",
            agentId);
        await ExecuteAsync(
            dataSource,
            "INSERT INTO widgets (id, public_key, name, agent_id, enabled, settings, created_at, updated_at) VALUES ($1, 'wk_people', 'Shop', $2, true, '{}', now(), now())",
            widgetId,
            agentId);
        await ExecuteAsync(
            dataSource,
            "INSERT INTO widget_visitors (id, widget_id, name, email, created_at, last_seen_at) VALUES "
            + "($1, $3, 'Jane', 'jane@example.com', '2026-10-01T08:00:00Z', '2026-11-01T08:00:00Z'), ($2, $3, NULL, NULL, '2026-10-02T08:00:00Z', '2026-10-02T09:00:00Z')",
            jane,
            anonymous,
            widgetId);
        foreach (var visitor in new[] { jane, jane, anonymous })
        {
            await ExecuteAsync(
                dataSource,
                "INSERT INTO conversations (id, title, agent_name, channel, status, widget_id, visitor_id, system_prompt, source_ids, created_at, updated_at, pinned) "
                + "VALUES ($1, 'Chat', 'orchestrator', 'Widget', 'Closed', $2, $3, '', '[]', now(), now(), false)",
                Guid.NewGuid(),
                widgetId,
                visitor);
        }

        await ExecuteAsync(
            dataSource,
            "INSERT INTO conversations (id, title, agent_name, channel, status, system_prompt, source_ids, created_at, updated_at, pinned) "
            + "VALUES ($1, 'Playground', 'orchestrator', 'Portal', 'Ai', '', '[]', now(), now(), false)",
            Guid.NewGuid());

        await migrator.MigrateAsync(cancellationToken: Ct);

        var contacts = await RowsAsync(
            dataSource,
            "SELECT id::text, coalesce(name, '-') || ' ' || coalesce(email, '-') || ' ' || conversation_count || ' ' || (external_id IS NULL) || ' ' || to_char(first_seen_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') FROM contacts");
        contacts.ShouldBe(
            new Dictionary<string, string>
            {
                [jane.ToString()] = "Jane jane@example.com 2 true 2026-10-01",
                [anonymous.ToString()] = "- - 1 true 2026-10-02",
            },
            ignoreOrder: true);
        (await RowsAsync(dataSource, "SELECT id::text, contact_id::text FROM widget_visitors")).ShouldAllBe(row => row.Key == row.Value);
        (await RowsAsync(dataSource, "SELECT id::text, coalesce(contact_id::text, '-') || ' ' || coalesce(visitor_id::text, '-') FROM conversations"))
            .Values.ShouldAllBe(value => value.Split(' ', StringSplitOptions.None)[0] == value.Split(' ', StringSplitOptions.None)[1]);
    }

    [Fact]
    public async Task Attributes_get_valid_keys_types_sources_and_a_registry_and_contacts_that_were_only_page_opens_go()
    {
        var connectionString = fixture.ConnectionStringFor($"ka_attributes_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261105131923_AddEvalItemSourceAndHistory", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var agentId = Guid.NewGuid();
        var widgetId = Guid.NewGuid();
        var ada = Guid.NewGuid();
        var grace = Guid.NewGuid();
        var empty = Guid.NewGuid();
        var emptyBrowser = Guid.NewGuid();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO agents (id, key, name, profile_name, folder_ids, tools, allow_general_knowledge, is_built_in, created_at, updated_at, handover_policy) "
            + "VALUES ($1, 'shop', 'Shop', 'orchestrator', '[]', '[]', false, false, now(), now(), 'OnRequest')",
            agentId);
        await ExecuteAsync(
            dataSource,
            "INSERT INTO widgets (id, public_key, name, agent_id, enabled, settings, created_at, updated_at) VALUES ($1, 'wk_attrs', 'Shop', $2, true, '{}', now(), now())",
            widgetId,
            agentId);
        await ExecuteAsync(
            dataSource,
            "INSERT INTO contacts (id, external_id, name, attributes, first_seen_at, last_seen_at, conversation_count) VALUES "
            + "($1, 'u1', 'Ada', '{\"Plan\": \"pro\", \"company.size\": \"12\", \"beta\": \"true\", \"email\": \"x\", \"9x\": \"y\"}', '2026-10-01T08:00:00Z', '2026-11-01T08:00:00Z', 0), "
            + "($2, 'u2', 'Grace', '{\"plan\": \"free\", \"company.size\": \"many\", \"beta\": \"false\"}', '2026-10-02T08:00:00Z', '2026-11-02T08:00:00Z', 0), "
            + "($3, NULL, NULL, '{}', '2026-10-03T08:00:00Z', '2026-10-03T08:00:00Z', 0)",
            ada,
            grace,
            empty);
        await ExecuteAsync(
            dataSource,
            "INSERT INTO widget_visitors (id, widget_id, contact_id, created_at, last_seen_at) VALUES ($1, $3, $2, now(), now()), ($4, $3, $5, now(), now()), ($6, $3, $5, now(), now())",
            emptyBrowser,
            empty,
            widgetId,
            Guid.NewGuid(),
            ada,
            Guid.NewGuid());

        await migrator.MigrateAsync(cancellationToken: Ct);

        (await RowsAsync(dataSource, "SELECT id::text, attributes::text FROM contacts")).ShouldBe(
            new Dictionary<string, string>
            {
                [ada.ToString()] = """{"beta": true, "plan": "pro", "company_size": "12", "email_custom": "x"}""",
                [grace.ToString()] = """{"beta": false, "plan": "free", "company_size": "many"}""",
            },
            ignoreOrder: true);
        (await RowsAsync(dataSource, "SELECT key, type || ' ' || label FROM contact_attributes")).ShouldBe(
            new Dictionary<string, string>
            {
                ["plan"] = "string Plan",
                ["beta"] = "boolean Beta",
                ["company_size"] = "string Company size",
                ["email_custom"] = "string Email custom",
            },
            ignoreOrder: true);
        (await RowsAsync(dataSource, "SELECT id::text, (attribute_sources -> 'plan' ->> 'source') || ' ' || session_count FROM contacts")).Values
            .ShouldBe(["verified 2", "verified 1"], ignoreOrder: true);
        (await RowsAsync(dataSource, "SELECT id::text, coalesce(contact_id::text, '-') || ' ' || session_count FROM widget_visitors WHERE id = '" + emptyBrowser + "'"))
            .ShouldBe(new Dictionary<string, string> { [emptyBrowser.ToString()] = "- 1" }, "the page open's browser stays, without the empty contact");

        var contact = await db.Contacts.AsNoTracking().SingleAsync(item => item.Id == ada, Ct);
        contact.Attributes["beta"].ValueKind.ShouldBe(System.Text.Json.JsonValueKind.True);
        contact.AttributeSources["plan"].UpdatedAt.ShouldBe(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));
    }

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql, params object[] values)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<Dictionary<string, string>> RowsAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new Dictionary<string, string>();
        while (await reader.ReadAsync(Ct))
        {
            rows[reader.GetString(0)] = reader.GetString(1);
        }

        return rows;
    }
}
