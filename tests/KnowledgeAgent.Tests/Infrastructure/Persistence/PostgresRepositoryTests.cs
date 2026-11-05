using System.Diagnostics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class PostgresRepositoryTests(PostgresPersistenceFixture fixture)
    : RepositoryContractTests<PostgresPersistenceFixture>(fixture), IClassFixture<PostgresPersistenceFixture>
{
    [Fact]
    public async Task Migrations_create_the_schema_on_an_empty_database_through_the_startup_initializer()
    {
        var connectionString = Fixture.ConnectionStringFor($"ka_migrations_{Guid.NewGuid():N}");
        using var host = PersistenceHost.Build(PostgresPersistenceFixture.PostgresSettings(connectionString));

        await PersistenceHost.RunStartupInitializersAsync(host.Services, Ct);
        // Idempotent: a second start finds nothing to apply.
        await PersistenceHost.RunStartupInitializersAsync(host.Services, Ct);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var applied = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();
        applied.ShouldBe(db.Database.GetMigrations());
        applied.ShouldContain(migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task The_migration_adds_the_default_labels_once()
    {
        var labels = await InScopeAsync((KnowledgeAgent.Features.Labels.ILabelRepository repository) => repository.ListAsync(Ct));

        labels.Where(label => label.Name is "Question" or "Problem" or "Feedback")
            .Select(label => (label.Name, label.AutoApply))
            .ShouldBe([("Feedback", true), ("Problem", true), ("Question", true)]);
        labels.Where(label => label.Name is "Question" or "Problem" or "Feedback").ShouldAllBe(label => label.Description.Length > 0);
    }

    [Fact]
    public async Task Values_protected_by_one_instance_can_be_read_by_another_sharing_the_database()
    {
        // Provider API keys saved in the portal must stay readable after restarts and container rebuilds.
        var settings = PostgresPersistenceFixture.PostgresSettings(Fixture.ConnectionStringFor($"ka_dataprotection_{Guid.NewGuid():N}"));
        string protectedValue;
        using (var first = PersistenceHost.Build(settings))
        {
            await PersistenceHost.RunStartupInitializersAsync(first.Services, Ct);
            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test").Protect("sk-ant-secret");
        }

        using var second = PersistenceHost.Build(settings);
        second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("restart-test").Unprotect(protectedValue).ShouldBe("sk-ant-secret");
    }

    [Fact]
    public async Task Source_lookups_are_served_by_trigram_indexes()
    {
        (await QueryAsync("SELECT extname, extversion FROM pg_extension WHERE extname = 'pg_trgm'")).ShouldContainKey("pg_trgm");
        var indexes = await QueryAsync("SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'sources'");
        foreach (var name in new[] { "ix_sources_title", "ix_sources_url", "ix_sources_file_name" })
        {
            indexes[name].ShouldContain("USING gin");
            indexes[name].ShouldContain("gin_trgm_ops");
        }

        var plan = await ExplainAsync(
            "SELECT id FROM sources WHERE title ILIKE '%handbook%' OR url ILIKE '%handbook%' OR file_name ILIKE '%handbook%'");
        plan.ShouldContain("ix_sources_title");
        plan.ShouldContain("ix_sources_url");
        plan.ShouldContain("ix_sources_file_name");
        plan.ShouldNotContain("Seq Scan");
    }

    [Fact]
    public async Task Schema_uses_snake_case_names_and_the_intended_column_types()
    {
        var columns = await QueryAsync(
            "SELECT table_name || '.' || column_name, data_type FROM information_schema.columns WHERE table_schema = 'public'");
        var indexes = await QueryAsync("SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'public'");
        var deleteRules = await QueryAsync("SELECT constraint_name, delete_rule FROM information_schema.referential_constraints WHERE constraint_schema = 'public'");

        columns.Keys.Select(column => column.Split('.')[0]).Distinct().Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            "__EFMigrationsHistory", "agent_api_keys", "agents", "alert_snoozes", "alert_states", "contact_attributes", "contacts", "conversation_handovers", "conversation_labels", "conversation_messages", "conversation_reads", "conversation_turns", "conversations",
            "data_protection_keys", "eval_dataset_items", "eval_datasets", "eval_runs", "feedback", "handover_inbound", "integration_settings", "labels", "mcp_servers", "model_profile_overrides",
            "provider_settings_overrides", "segments", "source_contents", "source_folders", "sources", "staff_members", "widget_assets", "widget_daily_usage",
            "widget_visitors", "widgets",
        });
        columns.ShouldSatisfyAllConditions(
            () => columns["conversation_turns.model_messages_json"].ShouldBe("text"),
            () => columns["conversation_turns.tool_calls_json"].ShouldBe("jsonb"),
            () => columns["conversation_turns.citations_json"].ShouldBe("jsonb"),
            () => columns["conversations.source_ids"].ShouldBe("jsonb"),
            () => columns["sources.metadata"].ShouldBe("jsonb"),
            () => columns["sources.crawl"].ShouldBe("jsonb"),
            () => columns["sources.status"].ShouldBe("text"),
            () => columns["sources.created_at"].ShouldBe("timestamp with time zone"),
            () => columns["sources.folder_id"].ShouldBe("uuid"),
            () => columns["sources.site"].ShouldBe("character varying"),
            () => columns["sources.stage"].ShouldBe("text"),
            () => columns["sources.embedded_chunks"].ShouldBe("integer"),
            () => columns["sources.job_run_id"].ShouldBe("text"),
            () => columns["source_folders.name"].ShouldBe("character varying"),
            () => columns["source_folders.parent_id"].ShouldBe("uuid"),
            () => columns["source_contents.raw_bytes"].ShouldBe("bytea"),
            () => columns["source_contents.extracted_text"].ShouldBe("text"),
            () => columns["model_profile_overrides.temperature"].ShouldBe("real"),
            () => columns["provider_settings_overrides.protected_api_key"].ShouldBe("text"),
            () => columns["provider_settings_overrides.default_embedding_dimensions"].ShouldBe("integer"),
            () => columns["data_protection_keys.xml"].ShouldBe("text"),
            () => columns["eval_runs.items_json"].ShouldBe("jsonb"),
            () => columns["eval_runs.request_json"].ShouldBe("jsonb"),
            () => columns["eval_runs.status"].ShouldBe("character varying"),
            () => columns["eval_runs.created_at"].ShouldBe("timestamp with time zone"),
            () => columns["agents.folder_ids"].ShouldBe("jsonb"),
            () => columns["agents.mcp_tools"].ShouldBe("jsonb"),
            () => columns["agents.specialists"].ShouldBe("jsonb"),
            () => columns["conversations.active_agent_id"].ShouldBe("uuid"),
            () => columns["mcp_servers.tools"].ShouldBe("jsonb"),
            () => columns["mcp_servers.protected_secret"].ShouldBe("text"),
            () => columns["agent_api_keys.hash"].ShouldBe("character varying"),
            () => columns["conversation_messages.citations_json"].ShouldBe("jsonb"),
            () => columns["conversations.widget_id"].ShouldBe("uuid"),
            () => columns["widgets.settings"].ShouldBe("jsonb"),
            () => columns["widget_assets.content"].ShouldBe("bytea"),
            () => columns["widget_daily_usage.day"].ShouldBe("date"),
            () => columns["feedback.reasons"].ShouldBe("jsonb"),
            () => columns["integration_settings.secrets_json"].ShouldBe("text"),
            () => columns["conversation_handovers.token"].ShouldBe("character varying"),
            () => columns["eval_dataset_items.expected_facts"].ShouldBe("jsonb"),
            () => columns["eval_dataset_items.history"].ShouldBe("jsonb"),
            () => columns["eval_dataset_items.source"].ShouldBe("character varying"),
            () => columns["eval_datasets.normalized_name"].ShouldBe("character varying"),
            () => columns["contacts.attributes"].ShouldBe("jsonb"),
            () => columns["contacts.attribute_sources"].ShouldBe("jsonb"),
            () => columns["contacts.session_count"].ShouldBe("integer"),
            () => columns["widget_visitors.session_count"].ShouldBe("integer"),
            () => columns["contact_attributes.key"].ShouldBe("character varying"),
            () => columns["contact_attributes.last_seen_at"].ShouldBe("timestamp with time zone"),
            () => columns["segments.filter"].ShouldBe("jsonb"),
            () => columns["contacts.external_id"].ShouldBe("character varying"),
            () => columns["widget_visitors.contact_id"].ShouldBe("uuid"),
            () => columns["conversations.contact_id"].ShouldBe("uuid"),
            () => columns["conversations.created_by_id"].ShouldBe("character varying"),
            () => columns["widgets.protected_identity_secret"].ShouldBe("text"));
        new[]
        {
            "pk_sources", "pk_source_contents", "pk_conversations", "pk_conversation_turns", "pk_model_profile_overrides",
            "pk_provider_settings_overrides", "pk_data_protection_keys", "ix_sources_title", "ix_sources_url", "ix_sources_file_name",
            "ix_sources_status", "ix_sources_created_at", "ix_conversations_updated_at", "ix_conversation_turns_conversation_id_sequence",
            "pk_eval_runs", "ix_eval_runs_created_at", "ix_eval_runs_dataset_created_at", "ix_eval_runs_status",
            "pk_source_folders", "ix_source_folders_parent_id", "ix_sources_folder_id", "ix_sources_site",
            "ix_agents_key", "ix_conversation_messages_conversation_id_sequence", "ix_widgets_public_key", "ix_conversation_handovers_token",
            "ix_conversations_visitor_id", "ix_conversations_widget_id_updated_at", "ix_conversations_created_at", "ix_conversation_turns_created_at",
            "ix_eval_datasets_normalized_name", "ix_contacts_external_id", "ix_contacts_last_seen_at_id", "ix_conversations_contact_id_updated_at",
            "ix_conversations_created_by_id", "ix_widget_visitors_contact_id", "ix_contacts_attributes", "pk_contact_attributes", "pk_segments",
            "pk_mcp_servers", "ix_mcp_servers_key", "pk_agent_api_keys", "ix_agent_api_keys_hash", "ix_agent_api_keys_agent_id",
        }.ShouldBeSubsetOf(indexes.Keys);
        indexes["ix_contacts_attributes"].ShouldContain("jsonb_path_ops");
        foreach (var name in new[] { "ix_conversations_title", "ix_conversation_messages_text", "ix_contacts_name", "ix_contacts_email" })
        {
            indexes[name].ShouldContain("gin_trgm_ops");
        }

        indexes["ix_eval_datasets_normalized_name"].ShouldContain("UNIQUE");
        indexes["ix_conversation_turns_conversation_id_sequence"].ShouldContain("UNIQUE");
        indexes["ix_conversation_messages_conversation_id_sequence"].ShouldContain("UNIQUE");
        indexes["ix_widgets_public_key"].ShouldContain("UNIQUE");
        indexes["ix_contacts_external_id"].ShouldContain("UNIQUE");
        indexes["ix_conversation_handovers_token"].ShouldContain("UNIQUE");
        indexes["ix_mcp_servers_key"].ShouldContain("UNIQUE");
        indexes["ix_agent_api_keys_hash"].ShouldContain("UNIQUE");
        deleteRules.ShouldBe(
            new Dictionary<string, string>
            {
                ["fk_conversation_turns_conversations_conversation_id"] = "CASCADE",
                ["fk_source_contents_sources_source_id"] = "CASCADE",
                ["fk_source_folders_source_folders_parent_id"] = "CASCADE",
                ["fk_sources_source_folders_folder_id"] = "SET NULL",
                ["fk_sources_sources_parent_id"] = "CASCADE",
                ["fk_conversation_messages_conversations_conversation_id"] = "CASCADE",
                ["fk_conversation_handovers_conversations_conversation_id"] = "CASCADE",
                ["fk_feedback_conversations_conversation_id"] = "CASCADE",
                ["fk_conversation_labels_conversations_conversation_id"] = "CASCADE",
                ["fk_conversation_labels_labels_label_id"] = "CASCADE",
                ["fk_conversation_reads_conversations_conversation_id"] = "CASCADE",
                ["fk_widget_assets_widgets_widget_id"] = "CASCADE",
                ["fk_widget_visitors_widgets_widget_id"] = "CASCADE",
                ["fk_widget_visitors_contacts_contact_id"] = "CASCADE",
                ["fk_conversations_contacts_contact_id"] = "SET NULL",
                ["fk_widget_daily_usage_widgets_widget_id"] = "CASCADE",
                ["fk_eval_dataset_items_eval_datasets_dataset_id"] = "CASCADE",
                ["fk_agent_api_keys_agents_agent_id"] = "CASCADE",
            },
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_site_migration_derives_the_site_of_existing_web_sources()
    {
        var connectionString = Fixture.ConnectionStringFor($"ka_sites_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028101324_AddSourceFolders", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var rows = new (string Title, string Type, string? Url)[]
        {
            ("www", "Url", "https://www.example.com/a"),
            ("port and query", "Url", "http://docs.python.org:8080/3/?q=www.x"),
            ("upper case and trailing dot", "Url", "https://WWW.Shop.Example./cart"),
            ("only www", "Url", "https://www./x"),
            ("pasted text", "Text", null),
        };
        foreach (var row in rows)
        {
            await using var insert = dataSource.CreateCommand(
                "INSERT INTO sources (id, type, status, title, url, chunk_count, token_estimate, attempts, created_at, updated_at, metadata) "
                + "VALUES ($1, $2, 'Ready', $3, $4, 0, 0, 0, now(), now(), '{}')");
            insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
            insert.Parameters.Add(new NpgsqlParameter { Value = row.Type });
            insert.Parameters.Add(new NpgsqlParameter { Value = row.Title });
            insert.Parameters.Add(new NpgsqlParameter { Value = (object?)row.Url ?? DBNull.Value });
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        await using var select = dataSource.CreateCommand("SELECT title, coalesce(site, '-') FROM sources");
        await using var reader = await select.ExecuteReaderAsync(Ct);
        var sites = new Dictionary<string, string>();
        while (await reader.ReadAsync(Ct))
        {
            sites[reader.GetString(0)] = reader.GetString(1);
        }

        sites.ShouldBe(
            new Dictionary<string, string>
            {
                ["www"] = SourceDefaults.SiteOf(new Uri("https://www.example.com/a")),
                ["port and query"] = "docs.python.org",
                ["upper case and trailing dot"] = SourceDefaults.SiteOf(new Uri("https://WWW.Shop.Example./cart")),
                ["only www"] = SourceDefaults.SiteOf(new Uri("https://www./x")),
                ["pasted text"] = "-",
            },
            ignoreOrder: true);
        sites["www"].ShouldBe("example.com");
        sites["upper case and trailing dot"].ShouldBe("shop.example");
    }

    [Fact]
    public async Task The_progress_migration_counts_the_chunks_of_ready_sources_as_embedded()
    {
        var connectionString = Fixture.ConnectionStringFor($"ka_progress_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028183002_AddSourceSites", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        foreach (var (title, status, chunks) in new[] { ("ready", "Ready", 7), ("failed", "Failed", 3) })
        {
            await using var insert = dataSource.CreateCommand(
                "INSERT INTO sources (id, type, status, title, chunk_count, token_estimate, attempts, created_at, updated_at, metadata) "
                + "VALUES ($1, 'Text', $2, $3, $4, 0, 0, now(), now(), '{}')");
            insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
            insert.Parameters.Add(new NpgsqlParameter { Value = status });
            insert.Parameters.Add(new NpgsqlParameter { Value = title });
            insert.Parameters.Add(new NpgsqlParameter { Value = chunks });
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        await using var select = dataSource.CreateCommand("SELECT title, embedded_chunks, coalesce(stage, '-'), coalesce(job_run_id, '-') FROM sources");
        await using var reader = await select.ExecuteReaderAsync(Ct);
        var rows = new Dictionary<string, (int Embedded, string Stage, string JobRunId)>();
        while (await reader.ReadAsync(Ct))
        {
            rows[reader.GetString(0)] = (reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
        }

        rows.ShouldBe(new Dictionary<string, (int, string, string)> { ["ready"] = (7, "-", "-"), ["failed"] = (0, "-", "-") }, ignoreOrder: true);
    }

    [Fact]
    public async Task The_cleanup_migration_deletes_settings_that_name_the_removed_providers()
    {
        var connectionString = Fixture.ConnectionStringFor($"ka_offline_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261030123024_DropJobTablesAddIngestionProgress", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var seed = dataSource.CreateCommand(
            "INSERT INTO model_profile_overrides (name, provider, updated_at) VALUES ('utility', 'offline', now()), ('embedding', 'HASH', now()), ('orchestrator', 'anthropic', now());"
            + "INSERT INTO provider_settings_overrides (key, updated_at) VALUES ('offline', now()), ('hash', now()), ('openai', now());"))
        {
            await seed.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        (await ColumnAsync(dataSource, "SELECT name FROM model_profile_overrides")).ShouldBe(["orchestrator"]);
        (await ColumnAsync(dataSource, "SELECT key FROM provider_settings_overrides")).ShouldBe(["openai"]);
    }

    [Fact]
    public async Task The_timeline_migration_turns_every_saved_turn_into_messages()
    {
        var connectionString = Fixture.ConnectionStringFor($"ka_timeline_{Guid.NewGuid():N}");
        await using var db = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261031150659_RemoveOfflineProviders", Ct);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var conversationId = Guid.NewGuid();
        const string citations = """[{"ref":"S1","sourceId":"5d1c0e4e-0b8f-4a59-9d6e-1f0d7b0c1a11","sourceTitle":"Notes","snippet":"x"}]""";
        await using (var insertConversation = dataSource.CreateCommand(
            "INSERT INTO conversations (id, title, agent_name, system_prompt, source_ids, created_at, updated_at) "
            + "VALUES ($1, 'Old chat', 'orchestrator', 'You are helpful.', '[]', now(), now())"))
        {
            insertConversation.Parameters.Add(new NpgsqlParameter { Value = conversationId });
            await insertConversation.ExecuteNonQueryAsync(Ct);
        }

        await using (var insertTurns = dataSource.CreateCommand(
            "INSERT INTO conversation_turns (id, conversation_id, sequence, user_message, assistant_message, model_messages_json, tool_calls_json, citations_json, error, duration_ms, created_at) VALUES "
            + "(gen_random_uuid(), $1, 1, 'Q1', 'A1 [S1]', '[]', '[]', $2::jsonb, NULL, 10, now() - interval '3 minutes'),"
            + "(gen_random_uuid(), $1, 2, 'Q2', '', '[]', '[]', '[]', 'The model failed.', 10, now() - interval '2 minutes'),"
            + "(gen_random_uuid(), $1, 3, 'Q3', '', '[]', '[]', '[]', NULL, 10, now() - interval '1 minute')"))
        {
            insertTurns.Parameters.Add(new NpgsqlParameter { Value = conversationId });
            insertTurns.Parameters.Add(new NpgsqlParameter { Value = citations });
            await insertTurns.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        await using var context = new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(connectionString).Options);
        var conversation = await context.Conversations.Include(c => c.Messages).Include(c => c.Turns).SingleAsync(Ct);
        conversation.Channel.ShouldBe(ConversationChannel.Portal);
        conversation.Status.ShouldBe(ConversationStatus.Ai);
        conversation.AgentId.ShouldBeNull();
        var messages = conversation.Messages.OrderBy(message => message.Sequence).ToList();
        messages.Select(message => (message.Sequence, message.Author, message.Text)).ShouldBe(
        [
            (1, MessageAuthor.User, "Q1"),
            (2, MessageAuthor.Assistant, "A1 [S1]"),
            (3, MessageAuthor.User, "Q2"),
            (4, MessageAuthor.System, "The model failed."),
            (5, MessageAuthor.User, "Q3"),
        ]);
        messages[1].CitationsJson.ShouldContain("\"S1\"");
        messages[0].TurnId.ShouldBe(messages[1].TurnId);
        messages.Select(message => message.TurnId).Distinct().ShouldBe(conversation.Turns.OrderBy(turn => turn.Sequence).Select(turn => (Guid?)turn.Id));
    }

    private static async Task<List<string>> ColumnAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var values = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [Fact]
    public async Task Stored_enums_are_strings_and_timestamps_are_utc()
    {
        var source = NewSource(SourceStatus.Processing, new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)));
        await InScopeAsync((ISourceRepository sources) => sources.AddAsync(source, cancellationToken: Ct));

        var row = await QueryAsync($"SELECT status || '|' || type, to_char(created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') FROM sources WHERE id = '{source.Id}'");

        row.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, string>("Processing|Url", "2025-01-01 18:04:05"));
    }

    [Fact]
    public async Task Concurrent_turns_get_distinct_consecutive_sequences()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var turns = Enumerable.Range(0, 6).Select(i => NewTurn(conversation.Id, userMessage: $"message {i}")).ToList();
        await Task.WhenAll(turns.Select(turn => Task.Run(
            () => InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(turn, Ct)), Ct)));

        var stored = await GetConversationAsync(conversation.Id);
        stored.Turns.Select(turn => turn.Sequence).ShouldBe(Enumerable.Range(1, 6));
        turns.Select(turn => turn.Sequence).Order().ShouldBe(Enumerable.Range(1, 6));
    }

    [Fact]
    public async Task AddTurn_takes_the_next_sequence_when_a_concurrent_writer_got_there_first()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        var competing = NewTurn(conversation.Id, sequence: 1, userMessage: "competing");
        var logger = new FakeLogger<ConversationRepository>();
        await using var db = InterceptedContext(() =>
            InScopeAsync((IConversationRepository conversations) => conversations.AddTurnAsync(competing, Ct)));
        var turn = NewTurn(conversation.Id, userMessage: "mine");

        await new ConversationRepository(db, new DbContextGate(), Fixture.Time, logger).AddTurnAsync(turn, Ct);

        turn.Sequence.ShouldBe(2);
        logger.Collector.GetSnapshot().ShouldHaveSingleItem().Message.ShouldContain("taken by a concurrent writer");
        (await GetConversationAsync(conversation.Id)).Turns.Select(stored => (stored.Sequence, stored.UserMessage))
            .ShouldBe(new[] { (1, "competing"), (2, "mine") });
    }

    [Fact]
    public async Task AddTurn_reports_a_conversation_deleted_after_the_existence_check_as_not_found()
    {
        var conversation = NewConversation();
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        await using var db = InterceptedContext(() =>
            InScopeAsync((IConversationRepository conversations) => conversations.DeleteAsync(conversation.Id, Ct)));
        var repository = new ConversationRepository(db, new DbContextGate(), Fixture.Time, new FakeLogger<ConversationRepository>());

        await Should.ThrowAsync<KeyNotFoundException>(() => repository.AddTurnAsync(NewTurn(conversation.Id), Ct));
    }

    [Fact]
    public async Task Startup_fails_fast_on_non_transient_errors()
    {
        var wrongPassword = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Password = "not-the-password" }.ConnectionString;
        using var host = PersistenceHost.Build(PostgresPersistenceFixture.PostgresSettings(wrongPassword));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<PostgresException>(() => PersistenceHost.RunStartupInitializersAsync(host.Services, Ct));

        exception.SqlState.ShouldBe(PostgresErrorCodes.InvalidPassword);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1), "no retry delay for errors that cannot heal");
    }

    private KnowledgeDbContext InterceptedContext(Func<Task> interference) =>
        new(new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(new BeforeFirstSave(interference))
            .Options);

    private async Task<string> ExplainAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(Fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(Ct);

        // Sequential scans are disabled so the plan does not depend on the table's size.
        await using (var disable = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await disable.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand("EXPLAIN " + sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var lines = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    private async Task<Dictionary<string, string>> QueryAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(Fixture.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(Ct))
        {
            rows[reader.GetString(0)] = reader.GetString(1);
        }

        return rows;
    }

    private sealed class BeforeFirstSave(Func<Task> interference) : SaveChangesInterceptor
    {
        private int saves;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == 1)
            {
                await interference();
            }

            return result;
        }
    }
}
