namespace KnowledgeAgent.Features.Chat.Live;

// At most once, to every other instance. Transport failures are logged, not thrown.
public interface ILiveBus
{
    Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default);
}

// One instance: the hub has it already.
internal sealed class InProcessLiveBus : ILiveBus
{
    public Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
