namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public interface IModelProfileOverrideStore
{
    Task<IReadOnlyList<ModelProfileOverride>> ListAsync(CancellationToken cancellationToken = default);
    Task<ModelProfileOverride?> GetAsync(string name, CancellationToken cancellationToken = default);
    Task UpsertAsync(ModelProfileOverride profileOverride, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
}
