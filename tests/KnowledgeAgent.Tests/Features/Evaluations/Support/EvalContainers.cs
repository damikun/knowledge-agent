using KnowledgeAgent.TestKit;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;

namespace KnowledgeAgent.Tests.Features.Evaluations.Support;

public sealed class EvalContainers : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PostgreSqlContainer? _postgres;
    private ClickHouseContainer? _clickHouse;

    public async Task<IReadOnlyDictionary<string, string?>> CreateStorageAsync()
    {
        await _gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_postgres is null || _clickHouse is null)
            {
                var postgres = new PostgreSqlBuilder(ContainerImages.Postgres).WithUsername("postgres").WithPassword("postgres").WithDatabase("postgres").Build();
                var clickHouse = new ClickHouseBuilder(ContainerImages.ClickHouse).WithUsername("clickhouse").WithPassword("clickhouse").Build();
                await Task.WhenAll(postgres.StartAsync(TestContext.Current.CancellationToken), clickHouse.StartAsync(TestContext.Current.CancellationToken));
                (_postgres, _clickHouse) = (postgres, clickHouse);
            }
        }
        finally
        {
            _gate.Release();
        }

        var database = $"ka_eval_{Guid.NewGuid():N}";
        return new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "Postgres",
            ["Persistence:MigrateOnStartup"] = "true",
            ["ConnectionStrings:knowledgedb"] =
                $"Host={_postgres.Hostname};Port={_postgres.GetMappedPublicPort(5432)};Database={database};Username=postgres;Password=postgres",
            ["VectorStore:Provider"] = "ClickHouse",
            ["VectorStore:EnsureSchemaOnStartup"] = "true",
            ["VectorStore:Database"] = database,
            ["ConnectionStrings:clickhouse"] =
                $"Host={_clickHouse.Hostname};Port={_clickHouse.GetMappedPublicPort(8123)};Username=clickhouse;Password=clickhouse",
        };
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_clickHouse is not null)
        {
            await _clickHouse.DisposeAsync();
        }

        _gate.Dispose();
    }
}
