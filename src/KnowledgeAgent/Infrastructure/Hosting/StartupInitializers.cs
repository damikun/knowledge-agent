using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.Hosting;

public static class StartupInitializers
{
    public static async Task RunStartupInitializersAsync(
        this IServiceProvider services,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        logger ??= services.GetRequiredService<ILoggerFactory>().CreateLogger("KnowledgeAgent.Startup");
        await using var scope = services.CreateAsyncScope();

        foreach (var initializer in scope.ServiceProvider.GetServices<IStartupInitializer>().OrderBy(i => i.Order))
        {
            try
            {
                logger.LogInformation("Running startup initializer {Initializer}", initializer.Name);
                await initializer.InitializeAsync(cancellationToken);
            }
            catch (Exception ex) when (!initializer.IsCritical)
            {
                logger.LogWarning(ex, "Startup initializer {Initializer} failed; continuing", initializer.Name);
            }
        }
    }
}
