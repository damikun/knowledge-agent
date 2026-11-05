using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Help;

internal sealed partial class HelpIndexInitializer(HelpLibrary library, ILogger<HelpIndexInitializer> logger) : IStartupInitializer
{
    public int Order => StartupOrder.Ingestion + 20;

    public string Name => "Help index";

    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var index = await library.ReloadAsync(cancellationToken);
        LogIndexed(logger, index.Sections.Count, index.Version);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Help: {Count} sections of version {Version} indexed in memory")]
    private static partial void LogIndexed(ILogger logger, int count, string version);
}
