using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class AssigneeBackfillMigrationTests(PostgresPersistenceFixture fixture) : IClassFixture<PostgresPersistenceFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_name_of_whoever_joined_last_owns_the_conversation_unless_the_agent_has_it_again()
    {
        var connectionString = fixture.ConnectionStringFor($"ka_assignees_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[migrations.IndexOf("20261105131013_BackfillConversationAssignees") - 1], Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var withPerson = await InsertConversationAsync(dataSource, "Human");
        var closed = await InsertConversationAsync(dataSource, "Closed");
        var backWithAgent = await InsertConversationAsync(dataSource, "Ai");
        var neverJoined = await InsertConversationAsync(dataSource, "HandoverPending");
        await InsertHandoverAsync(dataSource, withPerson, "Resolved", "Anna", "2026-11-01T08:00:00Z");
        await InsertHandoverAsync(dataSource, withPerson, "Active", "Petr", "2026-11-02T08:00:00Z");
        await InsertHandoverAsync(dataSource, closed, "Resolved", "Dana", "2026-11-03T08:00:00Z");
        await InsertHandoverAsync(dataSource, backWithAgent, "Resolved", "Eva", "2026-11-03T08:00:00Z");
        await InsertHandoverAsync(dataSource, neverJoined, "Pending", null, "2026-11-04T08:00:00Z");

        await migrator.MigrateAsync(cancellationToken: Ct);

        var rows = await RowsAsync(
            dataSource,
            "SELECT id::text, coalesce(assignee_name, '-') || ' ' || coalesce(assignee_id, '-') || ' ' || coalesce(to_char(assigned_at AT TIME ZONE 'UTC', 'YYYY-MM-DD'), '-') FROM conversations");
        rows.ShouldBe(
            new Dictionary<string, string>
            {
                [withPerson.ToString()] = "Petr - 2026-11-02",
                [closed.ToString()] = "Dana - 2026-11-03",
                [backWithAgent.ToString()] = "- - -",
                [neverJoined.ToString()] = "- - -",
            },
            ignoreOrder: true);
    }

    private static async Task<Guid> InsertConversationAsync(NpgsqlDataSource dataSource, string status)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO conversations (id, title, agent_name, channel, status, system_prompt, source_ids, created_at, updated_at, pinned) "
            + "VALUES ($1, 'Chat', 'orchestrator', 'Widget', $2, '', '[]', now(), now(), false)",
            id,
            status);
        return id;
    }

    private static Task InsertHandoverAsync(NpgsqlDataSource dataSource, Guid conversationId, string status, string? assignee, string requestedAt)
    {
        var at = DateTimeOffset.Parse(requestedAt, System.Globalization.CultureInfo.InvariantCulture);
        return ExecuteAsync(
            dataSource,
            "INSERT INTO conversation_handovers (id, conversation_id, channel, status, source, token, assignee, requested_at, accepted_at, updated_at) "
            + "VALUES ($1, $2, 'portal', $3, 'Agent', $4, $5, $6, $7, $6)",
            Guid.NewGuid(),
            conversationId,
            status,
            Guid.NewGuid().ToString("N")[..12],
            (object?)assignee ?? DBNull.Value,
            at,
            assignee is null ? DBNull.Value : at);
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
