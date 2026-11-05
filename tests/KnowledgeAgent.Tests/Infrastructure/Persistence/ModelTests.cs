using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class ModelTests
{
    [Fact]
    public void Npgsql_model_has_no_changes_missing_from_the_migrations()
    {
        using var db = new DesignTimeKnowledgeDbContextFactory().CreateDbContext([]);

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "add a migration: dotnet tool run dotnet-ef migrations add <Name> --project src/KnowledgeAgent");
        db.Database.GetMigrations().ShouldContain(migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public void Npgsql_model_uses_snake_case_and_replays_model_messages_from_a_text_column()
    {
        using var db = new DesignTimeKnowledgeDbContextFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        var turns = model.FindEntityType(typeof(ConversationTurn)).ShouldNotBeNull();

        turns.GetTableName().ShouldBe("conversation_turns");
        Column(turns, nameof(ConversationTurn.ModelMessagesJson)).ShouldBe(("model_messages_json", "text"));
        Column(turns, nameof(ConversationTurn.ToolCallsJson)).ShouldBe(("tool_calls_json", "jsonb"));
        Column(turns, nameof(ConversationTurn.CitationsJson)).ShouldBe(("citations_json", "jsonb"));
        turns.GetIndexes().Select(index => index.GetDatabaseName())
            .ShouldBe([ConversationTurnConfiguration.SequenceIndexName, "ix_conversation_turns_created_at"], ignoreOrder: true);
        Column(model.FindEntityType(typeof(Source))!, nameof(Source.Metadata)).ShouldBe(("metadata", "jsonb"));
        Column(model.FindEntityType(typeof(Conversation))!, nameof(Conversation.SourceIds)).ShouldBe(("source_ids", "jsonb"));
        Column(model.FindEntityType(typeof(SourceContent))!, nameof(SourceContent.RawBytes)).ShouldBe(("raw_bytes", "bytea"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Timestamps_are_normalized_to_utc_and_strings_drop_nul_on_every_provider(bool npgsql)
    {
        using var db = npgsql
            ? new DesignTimeKnowledgeDbContextFactory().CreateDbContext([])
            : new KnowledgeDbContext(new DbContextOptionsBuilder<KnowledgeDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var properties = db.Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()).ToList();

        var timestamps = properties.Where(property => property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?)).ToList();
        timestamps.Count.ShouldBe(65);
        timestamps.ShouldAllBe(property => property.GetValueConverter() is UtcDateTimeOffsetConverter);

        var strings = properties.Where(property => property.ClrType == typeof(string)).ToList();
        strings.ShouldNotBeEmpty();
        strings.ShouldAllBe(property => property.GetValueConverter() != null);
    }

    [Theory]
    [InlineData("ModelMessagesJson", "model_messages_json")]
    [InlineData("SourceId", "source_id")]
    [InlineData("PK_sources", "pk_sources")]
    [InlineData("FK_conversation_turns_conversations_conversation_id", "fk_conversation_turns_conversations_conversation_id")]
    [InlineData("HTMLParser", "html_parser")]
    [InlineData("Utf8Bytes", "utf8_bytes")]
    [InlineData("already_snake", "already_snake")]
    public void ToSnakeCase_converts_identifiers(string name, string expected) =>
        SnakeCaseNaming.ToSnakeCase(name).ShouldBe(expected);

    [Theory]
    [InlineData("""{"a":"x\u0000y"}""", """{"a":"xy"}""")]
    [InlineData("""{"a":"x\\u0000y"}""", """{"a":"x\\u0000y"}""")]
    [InlineData("""{"a":"x\\\u0000y"}""", """{"a":"x\\y"}""")]
    [InlineData("""{"k\u0000":"v"}""", """{"k":"v"}""")]
    [InlineData("""["\u0000","\\\\u0000"]""", """["","\\\\u0000"]""")]
    [InlineData("[\"raw\0nul\"]", """["rawnul"]""")]
    public void RemoveNulFromJson_drops_nul_escapes_but_keeps_escaped_backslashes(string json, string expected) =>
        StoreText.RemoveNulFromJson(json).ShouldBe(expected);

    [Fact]
    public void Text_without_nul_is_returned_as_is()
    {
        const string json = """{"a":"b"}""";

        StoreText.RemoveNulFromJson(json).ShouldBeSameAs(json);
        StoreText.RemoveNul("plain").ShouldBeSameAs("plain");
        StoreText.RemoveNul("a\0b\0").ShouldBe("ab");
    }

    [Fact]
    public void Json_column_comparers_compare_by_content_and_snapshot_deeply()
    {
        var dictionaries = JsonColumns.StringDictionaryComparer;
        var metadata = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var metadataSnapshot = dictionaries.Snapshot(metadata);
        metadata["a"] = "changed";

        dictionaries.Equals(metadata, metadataSnapshot).ShouldBeFalse();
        dictionaries.Equals(new() { ["b"] = "2", ["a"] = "1" }, new() { ["a"] = "1", ["b"] = "2" }).ShouldBeTrue();
        dictionaries.GetHashCode(new() { ["b"] = "2", ["a"] = "1" }).ShouldBe(dictionaries.GetHashCode(new() { ["a"] = "1", ["b"] = "2" }));

        var lists = JsonColumns.GuidListComparer;
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var idsSnapshot = lists.Snapshot(ids);
        ids.Reverse();

        lists.Equals(ids, idsSnapshot).ShouldBeFalse();
        lists.Equals(idsSnapshot, idsSnapshot.ToList()).ShouldBeTrue();
    }

    private static (string Column, string? Type) Column(IReadOnlyEntityType entityType, string propertyName)
    {
        var property = entityType.FindProperty(propertyName).ShouldNotBeNull();
        return (property.GetColumnName(), property.GetColumnType());
    }
}
