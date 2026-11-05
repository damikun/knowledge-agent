using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal sealed partial class AiOptionsSource(
    IOptionsMonitor<AiOptions> optionsMonitor,
    IServiceScopeFactory scopeFactory,
    ProviderApiKeyProtector protector,
    ILogger<AiOptionsSource> logger) : IAiOptionsSource
{
    // Not cached: a key saved in the portal must apply to the very next request.
    public async Task<AiOptions> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Apply(optionsMonitor.CurrentValue, await LoadSettingsAsync(cancellationToken), protector);

    internal async Task<IReadOnlyList<ProviderSettingsOverride>> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IProviderSettingsStore>() is not { } store)
        {
            return [];
        }

        try
        {
            return await store.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogStoreFailed(logger, ex);
            return [];
        }
    }

    internal static AiOptions Apply(AiOptions configured, IReadOnlyList<ProviderSettingsOverride> settings, ProviderApiKeyProtector protector)
    {
        var effective = new AiOptions
        {
            Profiles = new Dictionary<string, ModelProfileOptions>(configured.Profiles, StringComparer.OrdinalIgnoreCase),
            AutoChatProviders = configured.AutoChatProviders,
            AutoEmbeddingProviders = configured.AutoEmbeddingProviders,
            AutoRerankProviders = configured.AutoRerankProviders,
            Telemetry = configured.Telemetry,
        };

        foreach (var (key, provider) in configured.Providers)
        {
            effective.Providers[key] = Clone(provider);
        }

        foreach (var entry in settings)
        {
            // The portal edits configured providers, it does not create them.
            if (string.IsNullOrWhiteSpace(entry.Key) || !effective.Providers.TryGetValue(entry.Key.Trim(), out var provider))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(entry.ProtectedApiKey) && protector.TryUnprotect(entry.ProtectedApiKey, entry.Key) is { Length: > 0 } apiKey)
            {
                provider.ApiKey = apiKey;
            }

            if (!string.IsNullOrWhiteSpace(entry.Endpoint))
            {
                provider.Endpoint = entry.Endpoint.Trim();
            }

            if (!string.IsNullOrWhiteSpace(entry.DefaultChatModel))
            {
                provider.DefaultChatModel = entry.DefaultChatModel.Trim();
            }

            if (!string.IsNullOrWhiteSpace(entry.DefaultEmbeddingModel))
            {
                provider.DefaultEmbeddingModel = entry.DefaultEmbeddingModel.Trim();
            }

            if (entry.DefaultEmbeddingDimensions is > 0)
            {
                provider.DefaultEmbeddingDimensions = entry.DefaultEmbeddingDimensions;
            }
        }

        return effective;
    }

    private static AiProviderOptions Clone(AiProviderOptions provider) => new()
    {
        Type = provider.Type,
        ApiKey = provider.ApiKey,
        Endpoint = provider.Endpoint,
        DefaultChatModel = provider.DefaultChatModel,
        DefaultEmbeddingModel = provider.DefaultEmbeddingModel,
        DefaultEmbeddingDimensions = provider.DefaultEmbeddingDimensions,
        DefaultRerankModel = provider.DefaultRerankModel,
        Settings = new Dictionary<string, string>(provider.Settings, StringComparer.OrdinalIgnoreCase),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read provider settings saved in the portal; using configuration only")]
    private static partial void LogStoreFailed(ILogger logger, Exception exception);
}
