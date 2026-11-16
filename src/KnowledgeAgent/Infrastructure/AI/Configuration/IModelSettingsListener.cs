namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public interface IModelSettingsListener
{
    Task OnModelSettingsChangedAsync(CancellationToken cancellationToken);
}
