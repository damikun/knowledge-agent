using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Models;

namespace KnowledgeAgent.Features.SystemInfo;

public sealed record SystemInfoDto(
    string Version,
    string Environment,
    string PersistenceProvider,
    string VectorStoreProvider,
    string PromptStore,
    bool LangfuseConfigured,
    string? LangfuseUrl,
    bool ResearcherEnabled,
    IReadOnlyList<ProviderDto> Providers,
    PublicAddressDto PublicAddress);
