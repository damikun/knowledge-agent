using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Providers;

internal sealed partial class OneTimeWarnings(ILogger logger)
{
    private readonly ConcurrentDictionary<string, byte> _issued = new(StringComparer.Ordinal);

    public void Warn(string key, string message)
    {
        if (_issued.TryAdd(key, 0))
        {
            LogWarning(logger, message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogWarning(ILogger logger, string message);
}
