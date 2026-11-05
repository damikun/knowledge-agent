using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Prompts.Seeding;

internal sealed class PromptSeedingService(
    PromptSeeder seeder,
    PromptSeedingSettings settings,
    TimeProvider timeProvider,
    ILogger<PromptSeedingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!seeder.IsEnabled || seeder.IsCompleted)
        {
            return;
        }

        var startedAt = timeProvider.GetUtcNow();
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested && !seeder.IsCompleted)
        {
            // The startup initializer may have tried a moment ago, so the interval is kept.
            if (seeder.LastAttemptAt is { } lastAttempt)
            {
                var wait = lastAttempt + settings.RetryInterval - timeProvider.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, timeProvider, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            attempt++;
            using var timeout = new CancellationTokenSource(settings.AttemptTimeout, timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
            try
            {
                await seeder.SeedAsync(linked.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (timeProvider.GetUtcNow() - startedAt + settings.RetryInterval > settings.MaxDuration)
                {
                    logger.SeedingGaveUp(attempt, ex.Message);
                    return;
                }

                logger.SeedingAttemptFailed(attempt, ex.Message, settings.RetryInterval);
            }
        }
    }
}
