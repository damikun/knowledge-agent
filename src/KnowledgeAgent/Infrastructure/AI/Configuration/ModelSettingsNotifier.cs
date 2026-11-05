using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public sealed partial class ModelSettingsNotifier(IEnumerable<IModelSettingsListener> listeners, ILogger<ModelSettingsNotifier> logger)
{
    // The settings are saved by then, so a failing listener is logged rather than failing the request.
    public async Task NotifyAsync(CancellationToken cancellationToken)
    {
        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnModelSettingsChangedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogListenerFailed(logger, ex, listener.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model settings listener {Listener} failed")]
    private static partial void LogListenerFailed(ILogger logger, Exception exception, string listener);
}
