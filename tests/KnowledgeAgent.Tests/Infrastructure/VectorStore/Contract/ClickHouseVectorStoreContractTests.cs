using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Contract;

public sealed class ClickHouseVectorStoreContractTests(ClickHouseContainerFixture clickHouse) : VectorStoreContractTests
{
    protected override string ExpectedProviderName => "ClickHouse";

    protected override async Task<IVectorStore> CreateEmptyStoreAsync(CancellationToken cancellationToken)
    {
        var connectionString = await clickHouse.GetConnectionStringAsync(cancellationToken);
        var store = new ClickHouseVectorStore(
            connectionString,
            "knowledge_tests",
            $"chunks_{Guid.NewGuid():N}",
            NullLogger<ClickHouseVectorStore>.Instance);
        try
        {
            await store.EnsureSchemaAsync(cancellationToken);
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }
}
