using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class HandoverPolicyMigrationTests(PostgresPersistenceFixture fixture) : IClassFixture<PostgresPersistenceFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_handover_tool_becomes_the_agent_policy_and_leaves_tools_and_widget_settings()
    {
        var connectionString = fixture.ConnectionStringFor($"ka_handover_policy_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[migrations.IndexOf("20261105101130_AddAgentHandoverPolicy") - 1], Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        const string withTool = """["search_knowledge_base","request_human_handover"]""";
        await InsertAgentAsync(dataSource, "builtin", withTool, builtIn: true);
        var noTool = await InsertAgentAsync(dataSource, "no-tool", """["search_knowledge_base"]""");
        var noWidgets = await InsertAgentAsync(dataSource, "no-widgets", withTool);
        var unsure = await InsertAgentAsync(dataSource, "unsure", withTool);
        var onRequest = await InsertAgentAsync(dataSource, "on-request", withTool);
        var mixed = await InsertAgentAsync(dataSource, "mixed", withTool);

        await InsertWidgetAsync(dataSource, noTool, handoverEnabled: true, offerWhenUnsure: true);
        await InsertWidgetAsync(dataSource, unsure, handoverEnabled: true, offerWhenUnsure: true);
        await InsertWidgetAsync(dataSource, onRequest, handoverEnabled: true, offerWhenUnsure: false);

        // Only the widget that offered a person decided; the other one's offerWhenUnsure never applied.
        await InsertWidgetAsync(dataSource, mixed, handoverEnabled: true, offerWhenUnsure: false);
        await InsertWidgetAsync(dataSource, mixed, handoverEnabled: false, offerWhenUnsure: true);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var agents = await RowsAsync(dataSource, "SELECT key, handover_policy || ' ' || tools::text FROM agents");
        agents.ShouldBe(
            new Dictionary<string, string>
            {
                ["builtin"] = """Never ["search_knowledge_base"]""",
                ["no-tool"] = """Never ["search_knowledge_base"]""",
                ["no-widgets"] = """OnRequest ["search_knowledge_base"]""",
                ["unsure"] = """WhenUnsure ["search_knowledge_base"]""",
                ["on-request"] = """OnRequest ["search_knowledge_base"]""",
                ["mixed"] = """OnRequest ["search_knowledge_base"]""",
            },
            ignoreOrder: true);
        var leftovers = await RowsAsync(dataSource, "SELECT id::text, (settings -> 'handover' ? 'offerWhenUnsure')::text FROM widgets");
        leftovers.Values.ShouldAllBe(value => value == "false");
    }

    private static async Task<Guid> InsertAgentAsync(NpgsqlDataSource dataSource, string key, string tools, bool builtIn = false)
    {
        var id = Guid.NewGuid();
        await using var insert = dataSource.CreateCommand(
            "INSERT INTO agents (id, key, name, profile_name, folder_ids, tools, allow_general_knowledge, is_built_in, created_at, updated_at) "
            + "VALUES ($1, $2, $2, 'orchestrator', '[]', $3::jsonb, false, $4, now(), now())");
        insert.Parameters.Add(new NpgsqlParameter { Value = id });
        insert.Parameters.Add(new NpgsqlParameter { Value = key });
        insert.Parameters.Add(new NpgsqlParameter { Value = tools });
        insert.Parameters.Add(new NpgsqlParameter { Value = builtIn });
        await insert.ExecuteNonQueryAsync(Ct);
        return id;
    }

    private static async Task InsertWidgetAsync(NpgsqlDataSource dataSource, Guid agentId, bool handoverEnabled, bool offerWhenUnsure)
    {
        var settings = $$$"""{"behavior":{"handoverEnabled":{{{(handoverEnabled ? "true" : "false")}}}},"handover":{"channel":"portal","offerWhenUnsure":{{{(offerWhenUnsure ? "true" : "false")}}}}}""";
        await using var insert = dataSource.CreateCommand(
            "INSERT INTO widgets (id, public_key, name, agent_id, enabled, settings, created_at, updated_at) "
            + "VALUES ($1, $2, 'Shop', $3, true, $4::jsonb, now(), now())");
        insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
        insert.Parameters.Add(new NpgsqlParameter { Value = $"wk_{Guid.NewGuid():N}" });
        insert.Parameters.Add(new NpgsqlParameter { Value = agentId });
        insert.Parameters.Add(new NpgsqlParameter { Value = settings });
        await insert.ExecuteNonQueryAsync(Ct);
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
