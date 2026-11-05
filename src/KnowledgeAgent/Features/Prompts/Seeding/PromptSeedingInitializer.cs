using System.Globalization;
using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Prompts.Seeding;

internal sealed class PromptSeedingInitializer(
    PromptSeeder seeder,
    PromptSeedingSettings settings,
    TimeProvider timeProvider,
    ILogger<PromptSeedingInitializer> logger) : IStartupInitializer
{
    public int Order => StartupOrder.Prompts;

    public string Name => "Prompts (seed default prompts into Langfuse)";

    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!seeder.IsEnabled)
        {
            logger.SeedingSkipped(seeder.DisabledReason!);
            return;
        }

        using var timeout = new CancellationTokenSource(settings.InitialAttemptTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await seeder.SeedAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var reason = timeout.IsCancellationRequested
                ? string.Create(CultureInfo.InvariantCulture, $"no answer within {settings.InitialAttemptTimeout.TotalSeconds:0.##} s")
                : ex.Message;
            logger.SeedingDeferred(reason);
        }
    }
}
