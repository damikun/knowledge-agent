namespace KnowledgeAgent.Infrastructure.Jobs;

public interface IJobQueue
{
    Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default);
}
