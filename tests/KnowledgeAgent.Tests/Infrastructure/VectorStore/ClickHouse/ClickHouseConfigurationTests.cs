using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.ClickHouse;

public sealed class ClickHouseConfigurationTests
{
    [Theory]
    [InlineData("knowledge")]
    [InlineData("chunks")]
    [InlineData("_private")]
    [InlineData("Chunks_V2")]
    [InlineData("a")]
    [InlineData("x1_2_3")]
    public void ValidIdentifiers_AreAccepted(string identifier)
    {
        ClickHouseIdentifier.IsValid(identifier).ShouldBeTrue();
        ClickHouseIdentifier.Validate(identifier, "VectorStore:Table").ShouldBe(identifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1chunks")]
    [InlineData("chunks-v2")]
    [InlineData("knowledge.chunks")]
    [InlineData("chunks; DROP TABLE users")]
    [InlineData("`chunks`")]
    [InlineData("chunks`")]
    [InlineData("chunks'")]
    [InlineData("chunks--")]
    [InlineData("chunks\n")]
    [InlineData(" chunks")]
    [InlineData("chunks ")]
    [InlineData("čeština")]
    public void InvalidIdentifiers_AreRejected(string identifier)
    {
        ClickHouseIdentifier.IsValid(identifier).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => ClickHouseIdentifier.Validate(identifier, "VectorStore:Table"))
            .Message.ShouldContain("is not a valid ClickHouse identifier (expected ^[A-Za-z_][A-Za-z0-9_]*$)");
    }

    [Fact]
    public void NullIdentifier_IsRejected() => ClickHouseIdentifier.IsValid(null).ShouldBeFalse();

    [Theory]
    [InlineData("bad-name", "chunks", "VectorStore:Database")]
    [InlineData("knowledge", "chunks;--", "VectorStore:Table")]
    public void Store_RejectsInvalidIdentifiers(string database, string table, string setting)
    {
        var exception = Should.Throw<ArgumentException>(
            () => new ClickHouseVectorStore("Host=localhost;Port=8123", database, table, NullLogger<ClickHouseVectorStore>.Instance));

        exception.ParamName.ShouldBe(setting);
    }

    [Fact]
    public void Sql_UsesQuotedFullyQualifiedNames_AndOnlyServerSideParameters()
    {
        var sql = new ClickHouseSql("kb", "chunks");

        sql.QualifiedTable.ShouldBe("`kb`.`chunks`");
        sql.CreateDatabase.ShouldBe("CREATE DATABASE IF NOT EXISTS `kb`");
        sql.CreateTable.ShouldStartWith("CREATE TABLE IF NOT EXISTS `kb`.`chunks`");
        sql.CreateTable.ShouldContain("ORDER BY (embedding_model, source_id, chunk_index)");
        sql.DeleteBySource.ShouldBe("DELETE FROM `kb`.`chunks` WHERE source_id = {sourceId:UUID}");
        sql.GetChunks.ShouldContain("LIMIT {take:UInt32} OFFSET {skip:UInt32}");

        var plain = sql.Search(filterSources: false, filterDistance: false);
        plain.ShouldContain("cosineDistance(embedding, {q:Array(Float32)})");
        plain.ShouldContain("PREWHERE embedding_model = {model:String}");
        plain.ShouldContain("dimensions = {dims:UInt16}");
        plain.ShouldContain("ORDER BY distance ASC");
        plain.ShouldContain("LIMIT {k:UInt32}");
        plain.ShouldNotContain("{ids:Array(UUID)}");
        plain.ShouldNotContain("{maxDistance:Float64}");

        var filtered = sql.Search(filterSources: true, filterDistance: true);
        filtered.ShouldContain("source_id IN {ids:Array(UUID)}");
        filtered.ShouldContain("WHERE distance <= {maxDistance:Float64}");
    }

    [Fact]
    public void ConnectionString_IsParsed_WithoutDatabaseOrSession()
    {
        var settings = ClickHouseConnectionSettings.Create(
            "Host=ch.local;Port=18123;Username=agent;Password=s3cret;Database=knowledge;UseSession=true");

        settings.Host.ShouldBe("ch.local");
        settings.Port.ShouldBe((ushort)18123);
        settings.Username.ShouldBe("agent");
        settings.Password.ShouldBe("s3cret");
        settings.Database.ShouldBeEmpty();
        settings.UseSession.ShouldBeFalse();
    }

    [Fact]
    public void AspireStyleEndpoint_IsAccepted()
    {
        var settings = ClickHouseConnectionSettings.Create("Endpoint=https://ch.example.com:8443/proxy;Username=agent;Password=x");

        settings.Protocol.ShouldBe("https");
        settings.Host.ShouldBe("ch.example.com");
        settings.Port.ShouldBe((ushort)8443);
        settings.Path.ShouldBe("/proxy");
        settings.Username.ShouldBe("agent");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Password=s3cret;this is not a connection string")]
    [InlineData("Endpoint=not a url;Password=s3cret")]
    [InlineData("Endpoint=ftp://ch.example.com:21;Password=s3cret")]
    public void InvalidConnectionStrings_FailWithoutRevealingSecrets(string connectionString)
    {
        var exception = Should.Throw<InvalidOperationException>(() => ClickHouseConnectionSettings.Create(connectionString));

        exception.Message.ShouldStartWith("The ClickHouse connection string");
        exception.Message.ShouldNotContain("s3cret");
    }

    [Fact]
    public void OptionsValidator_AcceptsTheDefaults()
    {
        new VectorStoreOptionsValidator().Validate(null, new VectorStoreOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void OptionsValidator_RejectsAnUnknownProvider()
    {
        var result = new VectorStoreOptionsValidator().Validate(null, new VectorStoreOptions { Provider = "Pinecone" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldBe("VectorStore:Provider 'Pinecone' is not supported. Supported providers: ClickHouse, InMemory.");
    }

    [Fact]
    public void OptionsValidator_ReportsEveryClickHouseProblem()
    {
        var result = new VectorStoreOptionsValidator().Validate(null, new VectorStoreOptions
        {
            Provider = "clickhouse",
            ConnectionStringName = " ",
            Database = "1db",
            Table = "my table",
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBe(3);
        result.FailureMessage.ShouldContain("VectorStore:ConnectionStringName is required");
        result.FailureMessage.ShouldContain("VectorStore:Database '1db'");
        result.FailureMessage.ShouldContain("VectorStore:Table 'my table'");
    }

    [Fact]
    public void OptionsValidator_IgnoresClickHouseSettings_ForTheInMemoryProvider()
    {
        var options = new VectorStoreOptions { Provider = "InMemory", Database = "not valid", Table = "" };

        new VectorStoreOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
    }
}
