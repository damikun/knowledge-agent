namespace KnowledgeAgent.Features.Chat.Live;

public interface ILiveBus
{
    Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default);
}

internal sealed class InProcessLiveBus : ILiveBus
{
    public Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
