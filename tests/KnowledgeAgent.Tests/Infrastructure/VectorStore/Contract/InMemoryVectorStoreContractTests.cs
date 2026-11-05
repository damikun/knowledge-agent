using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.InMemory;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Contract;

public sealed class InMemoryVectorStoreContractTests : VectorStoreContractTests
{
    protected override string ExpectedProviderName => "InMemory";

    protected override async Task<IVectorStore> CreateEmptyStoreAsync(CancellationToken cancellationToken)
    {
        var store = new InMemoryVectorStore();
        await store.EnsureSchemaAsync(cancellationToken);
        return store;
    }
}
