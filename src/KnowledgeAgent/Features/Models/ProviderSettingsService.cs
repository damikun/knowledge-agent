using System.Diagnostics;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Models;

// API keys are stored encrypted and never leave this class in plain text.
internal sealed partial class ProviderSettingsService(
    IOptionsMonitor<AiOptions> optionsMonitor,
    AiOptionsSource optionsSource,
    AiProviderRegistry providers,
    ProviderApiKeyProtector protector,
    IServiceScopeFactory scopeFactory,
    ModelSettingsNotifier notifier,
    ILogger<ProviderSettingsService> logger) : IProviderSettingsService
{
    internal static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const int MaxApiKeyLength = 512;
    private const int MaxModelLength = 200;
    private const int MaxDimensions = 16_384;

    public async Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var configured = optionsMonitor.CurrentValue;
        var saved = await optionsSource.LoadSettingsAsync(cancellationToken);
        var effective = AiOptionsSource.Apply(configured, saved, protector);

        return effective.Providers
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => ToDto(entry.Key, entry.Value, configured.Providers[entry.Key], FindSaved(saved, entry.Key)))
            .ToList();
    }

    public async Task<ProviderDto?> UpdateAsync(string providerKey, UpdateProviderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryFindConfigured(providerKey, out var key, out var configuredProvider))
        {
            return null;
        }

        var provider = providers.Find(configuredProvider.Type);
        var values = Validate(key, provider, request);

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProviderSettingsStore>();
            var current = await store.GetAsync(key, cancellationToken);

            var settings = new ProviderSettingsOverride
            {
                Key = current?.Key ?? key,
                ProtectedApiKey = current?.ProtectedApiKey,
                ApiKeyHint = current?.ApiKeyHint,
                Endpoint = values.Endpoint,
                DefaultChatModel = values.DefaultChatModel,
                DefaultEmbeddingModel = values.DefaultEmbeddingModel,
                DefaultEmbeddingDimensions = values.DefaultEmbeddingDimensions,
            };

            if (values.ApiKey is not null)
            {
                settings.ProtectedApiKey = protector.Protect(values.ApiKey);
                settings.ApiKeyHint = ProviderApiKeyProtector.Hint(values.ApiKey);
            }
            else if (request.ClearApiKey)
            {
                settings.ProtectedApiKey = null;
                settings.ApiKeyHint = null;
            }

            if (IsEmpty(settings))
            {
                await store.DeleteAsync(key, cancellationToken);
            }
            else
            {
                await store.UpsertAsync(settings, cancellationToken);
            }
        }

        LogSaved(logger, key, values.ApiKey is not null, request.ClearApiKey);
        await notifier.NotifyAsync(cancellationToken);
        return await GetAsync(key, cancellationToken);
    }

    public async Task<ProviderDto?> ResetAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        if (!TryFindConfigured(providerKey, out var key, out _))
        {
            return null;
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProviderSettingsStore>().DeleteAsync(key, cancellationToken);
        }

        LogReset(logger, key);
        await notifier.NotifyAsync(cancellationToken);
        return await GetAsync(key, cancellationToken);
    }

    public async Task<ProviderTestResultDto?> TestAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        if (!TryFindConfigured(providerKey, out var key, out _))
        {
            return null;
        }

        var effective = await optionsSource.GetCurrentAsync(cancellationToken);
        var options = effective.Providers[key];
        var started = Stopwatch.GetTimestamp();

        ProviderTestResultDto Result(bool ok, string message, IReadOnlyList<string>? models = null) =>
            new(key, ok, message, models ?? [], (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        if (providers.Find(options.Type) is not { } provider)
        {
            return Result(false, $"Unknown provider type '{options.Type}'. Known types: {providers.KnownTypes}.");
        }

        if (!provider.IsConfigured(options))
        {
            return Result(false, $"Not configured: {AiProviderRegistry.DescribeMissingConfiguration(provider, key, options)}.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TestTimeout);
        try
        {
            var test = await provider.TestConnectionAsync(options, timeout.Token);
            return Result(test.Ok, Redact(test.Message, options.ApiKey), test.Models);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(false, $"No answer within {TestTimeout.TotalSeconds:0} seconds; check the endpoint and your network.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(logger, key, ex.GetType().Name);
            return Result(false, Redact(Describe(ex), options.ApiKey));
        }
    }

    private async Task<ProviderDto?> GetAsync(string key, CancellationToken cancellationToken) =>
        (await ListAsync(cancellationToken)).FirstOrDefault(dto => string.Equals(dto.Key, key, StringComparison.OrdinalIgnoreCase));

    private bool TryFindConfigured(string? providerKey, out string key, out AiProviderOptions provider)
    {
        key = string.Empty;
        provider = null!;
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return false;
        }

        foreach (var (candidate, options) in optionsMonitor.CurrentValue.Providers)
        {
            if (string.Equals(candidate, providerKey.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                provider = options;
                return true;
            }
        }

        return false;
    }

    private ProviderDto ToDto(string key, AiProviderOptions effective, AiProviderOptions configured, ProviderSettingsOverride? saved)
    {
        var provider = providers.Find(effective.Type);
        var usesApiKey = provider?.UsesApiKey ?? false;

        // A saved key is active only if it decrypts, otherwise the configured key applies.
        var portalKey = saved?.ProtectedApiKey is { Length: > 0 } protectedKey ? protector.TryUnprotect(protectedKey, key) : null;
        var (source, hint) = !usesApiKey
            ? ("none", (string?)null)
            : !string.IsNullOrEmpty(portalKey)
                ? ("portal", saved!.ApiKeyHint ?? ProviderApiKeyProtector.Hint(portalKey))
                : !string.IsNullOrWhiteSpace(configured.ApiKey)
                    ? ("configuration", ProviderApiKeyProtector.Hint(configured.ApiKey.Trim()))
                    : ("none", null);

        return new ProviderDto(
            key,
            effective.Type,
            provider?.IsConfigured(effective) ?? false,
            provider?.SupportsChat ?? false,
            provider?.SupportsEmbeddings ?? false,
            effective.DefaultChatModel,
            effective.DefaultEmbeddingModel,
            effective.DefaultEmbeddingDimensions,
            string.IsNullOrWhiteSpace(effective.Endpoint) ? null : effective.Endpoint.Trim(),
            usesApiKey,
            provider?.UsesEndpoint ?? false,
            provider?.EndpointRequired ?? false,
            source,
            hint,
            saved is not null,
            saved?.UpdatedAt,
            provider?.SupportsRerank ?? false,
            string.IsNullOrWhiteSpace(effective.DefaultRerankModel) ? null : effective.DefaultRerankModel.Trim());
    }

    private static ProviderSettingsOverride? FindSaved(IReadOnlyList<ProviderSettingsOverride> saved, string key) =>
        saved.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    private static bool IsEmpty(ProviderSettingsOverride settings) =>
        settings.ProtectedApiKey is null && settings.Endpoint is null && settings.DefaultChatModel is null
        && settings.DefaultEmbeddingModel is null && settings.DefaultEmbeddingDimensions is null;

    private static ValidatedValues Validate(string key, IAiProvider? provider, UpdateProviderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors[field] = [message];

        string? apiKey = null;
        if (request.ApiKey is not null)
        {
            apiKey = request.ApiKey.Trim();
            if (request.ClearApiKey)
            {
                Fail("apiKey", "Send either a new API key or clearApiKey, not both.");
            }
            else if (provider is not { UsesApiKey: true })
            {
                Fail("apiKey", $"Provider '{key}' does not use an API key.");
            }
            else if (apiKey.Length == 0)
            {
                Fail("apiKey", "The API key is empty. Use clearApiKey to remove the saved key.");
            }
            else if (apiKey.Length > MaxApiKeyLength || apiKey.Any(char.IsWhiteSpace) || apiKey.Any(char.IsControl))
            {
                Fail("apiKey", "That does not look like an API key (no spaces or line breaks, at most 512 characters).");
            }
        }

        var endpoint = Normalize(request.Endpoint);
        if (endpoint is not null)
        {
            if (provider is not { UsesEndpoint: true })
            {
                Fail("endpoint", $"Provider '{key}' does not use an endpoint.");
            }
            else if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Fail("endpoint", "Enter an absolute http(s) URL, e.g. http://localhost:11434.");
            }
        }

        var chatModel = Normalize(request.DefaultChatModel);
        if (chatModel is not null)
        {
            if (provider is not { SupportsChat: true })
            {
                Fail("defaultChatModel", $"Provider '{key}' does not offer chat models.");
            }
            else if (!IsModelId(chatModel))
            {
                Fail("defaultChatModel", "Enter a model id without spaces (at most 200 characters).");
            }
        }

        var embeddingModel = Normalize(request.DefaultEmbeddingModel);
        if (embeddingModel is not null)
        {
            if (provider is not { SupportsEmbeddings: true })
            {
                Fail("defaultEmbeddingModel", $"Provider '{key}' does not offer embedding models.");
            }
            else if (!IsModelId(embeddingModel))
            {
                Fail("defaultEmbeddingModel", "Enter a model id without spaces (at most 200 characters).");
            }
        }

        if (request.DefaultEmbeddingDimensions is { } dimensions)
        {
            if (provider is not { SupportsEmbeddings: true })
            {
                Fail("defaultEmbeddingDimensions", $"Provider '{key}' does not offer embedding models.");
            }
            else if (dimensions is < 1 or > MaxDimensions)
            {
                Fail("defaultEmbeddingDimensions", $"Dimensions must be between 1 and {MaxDimensions}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return new ValidatedValues(apiKey, endpoint, chatModel, embeddingModel, request.DefaultEmbeddingDimensions);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsModelId(string value) => value.Length <= MaxModelLength && !value.Any(char.IsWhiteSpace);

    private static string Describe(Exception exception)
    {
        var message = exception.InnerException is HttpRequestException inner && exception is not HttpRequestException
            ? inner.Message
            : exception.Message;
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message.Trim();
    }

    // Provider errors sometimes echo request details, so never let the key through.
    private static string Redact(string message, string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? message : message.Replace(apiKey.Trim(), "***", StringComparison.Ordinal);

    private sealed record ValidatedValues(string? ApiKey, string? Endpoint, string? DefaultChatModel, string? DefaultEmbeddingModel, int? DefaultEmbeddingDimensions);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provider settings saved for '{ProviderKey}' (new key: {NewKey}, key removed: {KeyRemoved})")]
    private static partial void LogSaved(ILogger logger, string providerKey, bool newKey, bool keyRemoved);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provider settings of '{ProviderKey}' reset to configuration")]
    private static partial void LogReset(ILogger logger, string providerKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection test of provider '{ProviderKey}' failed ({ExceptionType})")]
    private static partial void LogTestFailed(ILogger logger, string providerKey, string exceptionType);
}
