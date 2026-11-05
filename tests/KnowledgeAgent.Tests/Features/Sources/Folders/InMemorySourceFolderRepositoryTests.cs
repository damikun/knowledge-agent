using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;

namespace KnowledgeAgent.Tests.Features.Sources.Folders;

public sealed class InMemorySourceFolderRepositoryTests(InMemoryPersistenceFixture fixture)
    : SourceFolderRepositoryContractTests<InMemoryPersistenceFixture>(fixture), IClassFixture<InMemoryPersistenceFixture>;
