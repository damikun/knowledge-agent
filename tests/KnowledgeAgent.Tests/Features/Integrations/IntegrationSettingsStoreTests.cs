using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Integrations;

public sealed class IntegrationSettingsStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_that_races_a_save_does_not_cache_the_settings_from_before_it()
    {
        using var host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"integration-settings-{Guid.NewGuid():N}",
            },
            configure: builder =>
            {
                builder.Services.AddDataProtection();
                builder.Services.AddOptions<IntegrationOptions>();
                builder.Services.AddSingleton<IIntegrationSettings, IntegrationSettingsStore>();
            });
        var settings = host.Services.GetRequiredService<IIntegrationSettings>();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await settings.GetSlackAsync(Ct);
            }
        })).ToList();

        try
        {
            for (var i = 0; i < 200; i++)
            {
                var channel = $"C{i:D7}";
                await settings.SaveSlackAsync(new SlackSettings { Enabled = true, ChannelId = channel }, Ct);
                (await settings.GetSlackAsync(Ct)).ChannelId.ShouldBe(channel, $"save {i}");
            }
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(readers);
        }
    }
}
