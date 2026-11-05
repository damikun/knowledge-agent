using KnowledgeAgent.TestKit;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(KnowledgeAgent.Api.Tests.Support.ContainersLifetime))]

namespace KnowledgeAgent.Api.Tests.Support;

public static class SharedContainers
{
    private const string PostgresUser = "postgres";
    private const string PostgresPassword = "postgres";
    private const string ClickHouseUser = "clickhouse";
    private const string ClickHousePassword = "clickhouse";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _postgres;
    private static ClickHouseContainer? _clickHouse;
    private static Exception? _startFailure;

    public static async Task<IReadOnlyDictionary<string, string?>> CreateIsolatedStorageAsync()
    {
        var (postgres, clickHouse) = await EnsureStartedAsync();
        var database = $"ka_e2e_{Guid.NewGuid():N}";

        return new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "Postgres",
            ["Persistence:ConnectionStringName"] = "knowledgedb",
            ["Persistence:MigrateOnStartup"] = "true",
            ["ConnectionStrings:knowledgedb"] =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database={database};Username={PostgresUser};Password={PostgresPassword}",
            ["VectorStore:Provider"] = "ClickHouse",
            ["VectorStore:ConnectionStringName"] = "clickhouse",
            ["VectorStore:EnsureSchemaOnStartup"] = "true",
            ["VectorStore:Database"] = database,
            ["ConnectionStrings:clickhouse"] =
                $"Host={clickHouse.Hostname};Port={clickHouse.GetMappedPublicPort(8123)};Username={ClickHouseUser};Password={ClickHousePassword}",
        };
    }

    private static async Task<(PostgreSqlContainer Postgres, ClickHouseContainer ClickHouse)> EnsureStartedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_startFailure is not null)
            {
                throw new InvalidOperationException("The shared test containers failed to start earlier in this run.", _startFailure);
            }

            if (_postgres is null || _clickHouse is null)
            {
                // Every test app gets its own database on this server and test classes run in parallel, so the apps'
                // connection pools together outgrow the default limit of 100.
                var postgres = new PostgreSqlBuilder(ContainerImages.Postgres)
                    .WithUsername(PostgresUser)
                    .WithPassword(PostgresPassword)
                    .WithDatabase("postgres")
                    .WithCommand("-c", "max_connections=400")
                    .Build();
                var clickHouse = new ClickHouseBuilder(ContainerImages.ClickHouse)
                    .WithUsername(ClickHouseUser)
                    .WithPassword(ClickHousePassword)
                    .Build();

                try
                {
                    await Task.WhenAll(postgres.StartAsync(), clickHouse.StartAsync());
                }
                catch (Exception exception)
                {
                    _startFailure = exception;
                    await postgres.DisposeAsync();
                    await clickHouse.DisposeAsync();
                    throw;
                }

                _postgres = postgres;
                _clickHouse = clickHouse;
            }

            return (_postgres, _clickHouse);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_postgres is not null)
            {
                await _postgres.DisposeAsync();
                _postgres = null;
            }

            if (_clickHouse is not null)
            {
                await _clickHouse.DisposeAsync();
                _clickHouse = null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}

public sealed class ContainersLifetime : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => SharedContainers.DisposeAsync();
}
