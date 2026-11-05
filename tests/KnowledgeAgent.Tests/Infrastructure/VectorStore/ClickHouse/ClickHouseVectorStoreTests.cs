using System.Collections.Concurrent;
using System.Diagnostics;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using ClickHouse.Driver.Utility;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using static KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.TestChunks;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.ClickHouse;

public sealed class ClickHouseVectorStoreTests(ClickHouseContainerFixture clickHouse) : IDisposable
{
    private const string Database = "knowledge_store_tests";

    private readonly List<IDisposable> _disposables = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    [Fact]
    public async Task EnsureSchema_CreatesTheDocumentedTable()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = CreateStore(connectionString);

        await store.EnsureSchemaAsync(Ct);

        using var admin = new ClickHouseClient(connectionString);
        var columns = await QueryAsync(
            admin,
            "SELECT name, type, default_kind, default_expression FROM system.columns WHERE database = {db:String} AND table = {table:String} ORDER BY position",
            reader => string.Join(' ', new[] { reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3) }.Where(part => part.Length > 0)),
            ("db", Database),
            ("table", store.Table));
        columns.ShouldBe(
        [
            "chunk_id UUID",
            "source_id UUID",
            "chunk_index UInt32",
            "content String",
            "context String DEFAULT ''",
            "metadata String",
            "embedding_model LowCardinality(String)",
            "dimensions UInt16",
            "embedding Array(Float32)",
            "created_at DateTime64(3, 'UTC') DEFAULT now64(3)",
        ]);

        var table = await QueryAsync(
            admin,
            "SELECT engine, sorting_key FROM system.tables WHERE database = {db:String} AND name = {table:String}",
            reader => $"{reader.GetString(0)} ORDER BY ({reader.GetString(1)})",
            ("db", Database),
            ("table", store.Table));
        table.ShouldHaveSingleItem().ShouldBe("MergeTree ORDER BY (embedding_model, source_id, chunk_index)");
    }

    [Fact]
    public async Task EnsureSchema_RejectsAnIncompatibleExistingTable()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = CreateStore(connectionString);
        using var admin = new ClickHouseClient(connectionString);
        await admin.ExecuteNonQueryAsync($"CREATE DATABASE IF NOT EXISTS {Database}", cancellationToken: Ct);
        await admin.ExecuteNonQueryAsync(
            $"CREATE TABLE {Database}.{store.Table} (chunk_id UUID, source_id UUID, chunk_index UInt32, content String, " +
            "embedding_model String, dimensions UInt16, embedding Array(Float64)) ENGINE = MergeTree ORDER BY chunk_id",
            cancellationToken: Ct);

        var exception = await Should.ThrowAsync<VectorStoreSchemaException>(() => store.EnsureSchemaAsync(Ct));

        exception.Message.ShouldContain($"ClickHouse table {Database}.{store.Table} exists but does not match the vector store schema");
        exception.Message.ShouldContain("column 'metadata' is missing (expected String)");
        exception.Message.ShouldContain("column 'embedding_model' is String (expected LowCardinality(String))");
        exception.Message.ShouldContain("column 'embedding' is Array(Float64) (expected Array(Float32))");
    }

    [Fact]
    public async Task Insert_StoresDimensions_AndFillsCreatedAt()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = await CreateReadyStoreAsync(connectionString);
        var before = DateTime.UtcNow.AddSeconds(-5);

        await store.InsertAsync([Chunk(Guid.NewGuid(), 7, [1, 2, 3, 4, 5, 6])], Ct);

        using var admin = new ClickHouseClient(connectionString);
        var row = (await QueryAsync(
            admin,
            $"SELECT chunk_index, dimensions, length(embedding), created_at, metadata FROM {Database}.{store.Table}",
            reader => (Index: reader.GetValue(0), Dimensions: reader.GetValue(1), Length: reader.GetValue(2), CreatedAt: reader.GetDateTime(3), Metadata: reader.GetString(4)))).ShouldHaveSingleItem();
        Convert.ToInt32(row.Index).ShouldBe(7);
        Convert.ToInt32(row.Dimensions).ShouldBe(6);
        Convert.ToInt32(row.Length).ShouldBe(6);
        row.CreatedAt.ShouldBeGreaterThan(before);
        row.CreatedAt.ShouldBeLessThan(DateTime.UtcNow.AddSeconds(5));
        row.Metadata.ShouldBe("{}");
    }

    [Fact]
    public async Task DeleteBySource_IsALightweightDelete()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = await CreateReadyStoreAsync(connectionString);
        var sourceId = Guid.NewGuid();
        await store.InsertAsync([Chunk(sourceId, 0, [1, 0, 0, 0]), Chunk(sourceId, 1, [0, 1, 0, 0]), Chunk(Guid.NewGuid(), 0, [0, 0, 1, 0])], Ct);

        (await store.DeleteBySourceAsync(sourceId, Ct)).ShouldBe(2);

        // Lightweight deletes are recorded as a mutation that masks rows via the hidden _row_exists column.
        using var admin = new ClickHouseClient(connectionString);
        var mutations = await QueryAsync(
            admin,
            "SELECT command, is_done FROM system.mutations WHERE database = {db:String} AND table = {table:String}",
            reader => (Command: reader.GetString(0), IsDone: Convert.ToInt32(reader.GetValue(1))),
            ("db", Database),
            ("table", store.Table));
        var mutation = mutations.ShouldHaveSingleItem();
        mutation.Command.ShouldContain("_row_exists");
        mutation.IsDone.ShouldBe(1);
        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task DatabaseInTheConnectionString_IsIgnored()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var pointingElsewhere = new ClickHouseConnectionStringBuilder(connectionString) { Database = "database_that_does_not_exist" }.ConnectionString;
        var store = CreateStore(pointingElsewhere);

        await store.EnsureSchemaAsync(Ct);
        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0])], Ct);

        (await store.CountAsync(cancellationToken: Ct)).ShouldBe(1);
        (await store.PingAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Ping_ReturnsFalse_ForWrongCredentials()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var wrongPassword = new ClickHouseConnectionStringBuilder(connectionString) { Password = "wrong-password" }.ConnectionString;
        var store = CreateStore(wrongPassword);

        (await store.PingAsync(Ct)).ShouldBeFalse();
        await Should.ThrowAsync<ClickHouseServerException>(() => store.CountAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Ping_ReturnsFalse_WhenTheServerIsUnreachable()
    {
        // Nothing listens on port 1, so no Docker is needed.
        var store = CreateStore("Host=127.0.0.1;Port=1;Username=default;Password=");

        (await store.PingAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Search_HandlesHighDimensionalEmbeddings()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = await CreateReadyStoreAsync(connectionString);
        const string model = "test:large:3072";
        var random = new Random(7);
        var sourceId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 40).Select(i => Chunk(sourceId, i, RandomUnitVector(random, 3072), model)).ToArray();
        await store.InsertAsync(chunks, Ct);

        var hits = await store.SearchAsync(new VectorSearchQuery(chunks[21].Embedding, model, 3, [sourceId], MinScore: -1), Ct);

        hits.Count.ShouldBe(3);
        hits[0].ChunkId.ShouldBe(chunks[21].ChunkId);
        hits[0].Score.ShouldBe(1d, 1e-5);
    }

    [Fact]
    public async Task Hosting_WithTheClickHouseProvider_InitializesTheSchema_AndReportsHealthy()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var table = $"chunks_{Guid.NewGuid():N}";
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VectorStore:Provider"] = "ClickHouse",
            ["VectorStore:Database"] = Database,
            ["VectorStore:Table"] = table,
            ["ConnectionStrings:clickhouse"] = connectionString,
        });
        builder.AddVectorStore();
        using var host = builder.Build();

        foreach (var initializer in host.Services.GetServices<IStartupInitializer>().OrderBy(i => i.Order))
        {
            await initializer.InitializeAsync(Ct);
        }

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        report.Entries["vectorstore"].Status.ShouldBe(HealthStatus.Healthy);

        var store = host.Services.GetRequiredService<IVectorStore>();
        store.ShouldBeOfType<ClickHouseVectorStore>().Table.ShouldBe(table);
        var chunk = Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0]);
        await store.InsertAsync([chunk], Ct);
        (await store.SearchAsync(Query([1, 0, 0, 0]), Ct)).ShouldHaveSingleItem().ChunkId.ShouldBe(chunk.ChunkId);
    }

    [Fact]
    public async Task Operations_EmitClientSpans_AndMarkFailures()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var store = await CreateReadyStoreAsync(connectionString);
        var missingTable = CreateStore(connectionString);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "KnowledgeAgent.VectorStore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                // The listener is process-wide: keep only the spans of this test's tables.
                if (activity.GetTagItem("db.collection.name") is string table && (table == store.Table || table == missingTable.Table))
                {
                    spans.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await store.InsertAsync([Chunk(Guid.NewGuid(), 0, [1, 0, 0, 0])], Ct);
        await store.SearchAsync(Query([1, 0, 0, 0]), Ct);
        await Should.ThrowAsync<ClickHouseServerException>(() => missingTable.CountAsync(cancellationToken: Ct));

        spans.Select(span => span.DisplayName).ShouldBe(
        [
            $"insert {Database}.{store.Table}",
            $"search {Database}.{store.Table}",
            $"count {Database}.{missingTable.Table}",
        ]);
        var search = spans.ElementAt(1);
        search.Kind.ShouldBe(ActivityKind.Client);
        search.GetTagItem("db.system.name").ShouldBe("clickhouse");
        search.GetTagItem("db.namespace").ShouldBe(Database);
        search.GetTagItem("vectorstore.hits").ShouldBe(1);
        search.Status.ShouldBe(ActivityStatusCode.Unset);
        spans.Last().Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task EnsureSchema_adds_the_context_column_to_older_tables_and_keyword_search_reads_it()
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(Ct);
        var table = $"chunks_{Guid.NewGuid():N}";
        using (var admin = new ClickHouseClient(connectionString))
        {
            // The table as it was created before contextual retrieval.
            await admin.ExecuteNonQueryAsync($"CREATE DATABASE IF NOT EXISTS {Database}", cancellationToken: Ct);
            await admin.ExecuteNonQueryAsync(
                $"CREATE TABLE {Database}.{table} (chunk_id UUID, source_id UUID, chunk_index UInt32, content String, metadata String, "
                + "embedding_model LowCardinality(String), dimensions UInt16, embedding Array(Float32), created_at DateTime64(3, 'UTC') DEFAULT now64(3)) "
                + "ENGINE = MergeTree ORDER BY (embedding_model, source_id, chunk_index)",
                cancellationToken: Ct);
        }

        var store = CreateStore(connectionString, table);
        await store.EnsureSchemaAsync(Ct);
        var sourceId = Guid.NewGuid();
        await store.InsertAsync(
            [
                Chunk(sourceId, 0, [1, 0, 0, 0], content: "Paid back within 30 days.") with { Context = "Handbook > Refunds" },
                Chunk(sourceId, 1, [0, 1, 0, 0], content: "Opening hours are nine to five."),
            ],
            Ct);

        var hits = await store.KeywordSearchAsync(new KeywordSearchQuery(["refunds"], ModelA, 5), Ct);

        hits.ShouldHaveSingleItem().Content.ShouldBe("Paid back within 30 days.", "the context is searched, but not shown");
    }

    private ClickHouseVectorStore CreateStore(string connectionString, string? table = null)
    {
        var store = new ClickHouseVectorStore(connectionString, Database, table ?? $"chunks_{Guid.NewGuid():N}", NullLogger<ClickHouseVectorStore>.Instance);
        _disposables.Add(store);
        return store;
    }

    private async Task<ClickHouseVectorStore> CreateReadyStoreAsync(string connectionString)
    {
        var store = CreateStore(connectionString);
        await store.EnsureSchemaAsync(Ct);
        return store;
    }

    private static async Task<List<T>> QueryAsync<T>(
        ClickHouseClient client,
        string sql,
        Func<ClickHouseDataReader, T> map,
        params (string Name, object Value)[] parameters)
    {
        var collection = new ClickHouseParameterCollection();
        foreach (var (name, value) in parameters)
        {
            collection.AddParameter(name, value);
        }

        var rows = new List<T>();
        using var reader = await client.ExecuteReaderAsync(sql, collection, cancellationToken: Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
