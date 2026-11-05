namespace KnowledgeAgent.Features.Models;

public interface IProviderSettingsService
{
    Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProviderDto?> UpdateAsync(string providerKey, UpdateProviderRequest request, CancellationToken cancellationToken = default);

    Task<ProviderDto?> ResetAsync(string providerKey, CancellationToken cancellationToken = default);

    Task<ProviderTestResultDto?> TestAsync(string providerKey, CancellationToken cancellationToken = default);
}
