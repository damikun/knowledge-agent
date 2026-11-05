namespace KnowledgeAgent.Infrastructure.AI.Configuration;

// Called after a provider or a profile is changed at runtime, so features can react to the new models.
public interface IModelSettingsListener
{
    Task OnModelSettingsChangedAsync(CancellationToken cancellationToken);
}
