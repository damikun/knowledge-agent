namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public interface IAiOptionsSource
{
    Task<AiOptions> GetCurrentAsync(CancellationToken cancellationToken = default);
}
