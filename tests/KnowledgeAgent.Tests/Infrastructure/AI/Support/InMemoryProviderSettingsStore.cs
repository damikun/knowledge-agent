using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI.Configuration;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal sealed class InMemoryProviderSettingsState
{
    public ConcurrentDictionary<string, ProviderSettingsOverride> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Failure { get; set; }
}

internal sealed class InMemoryProviderSettingsStore(InMemoryProviderSettingsState state) : IProviderSettingsStore
{
    public Task<IReadOnlyList<ProviderSettingsOverride>> ListAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<ProviderSettingsOverride>>(state.Items.Values.Select(Copy).OrderBy(s => s.Key, StringComparer.Ordinal).ToList());
    }

    public Task<ProviderSettingsOverride?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult(state.Items.TryGetValue(key, out var settings) ? Copy(settings) : null);
    }

    public Task UpsertAsync(ProviderSettingsOverride settings, CancellationToken cancellationToken = default)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        state.Items[settings.Key] = Copy(settings);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.Items.TryRemove(key, out _));

    private void ThrowIfFailing()
    {
        if (state.Failure is { } failure)
        {
            throw failure;
        }
    }

    private static ProviderSettingsOverride Copy(ProviderSettingsOverride settings) => new()
    {
        Key = settings.Key,
        ProtectedApiKey = settings.ProtectedApiKey,
        ApiKeyHint = settings.ApiKeyHint,
        Endpoint = settings.Endpoint,
        DefaultChatModel = settings.DefaultChatModel,
        DefaultEmbeddingModel = settings.DefaultEmbeddingModel,
        DefaultEmbeddingDimensions = settings.DefaultEmbeddingDimensions,
        UpdatedAt = settings.UpdatedAt,
    };
}
