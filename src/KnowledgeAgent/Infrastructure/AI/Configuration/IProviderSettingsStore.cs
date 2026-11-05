namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public interface IProviderSettingsStore
{
    Task<IReadOnlyList<ProviderSettingsOverride>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProviderSettingsOverride?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task UpsertAsync(ProviderSettingsOverride settings, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
