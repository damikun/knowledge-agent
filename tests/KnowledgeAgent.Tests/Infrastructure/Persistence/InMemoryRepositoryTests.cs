using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class InMemoryRepositoryTests(InMemoryPersistenceFixture fixture)
    : RepositoryContractTests<InMemoryPersistenceFixture>(fixture), IClassFixture<InMemoryPersistenceFixture>;
