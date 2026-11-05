using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Chat;

internal sealed partial class ChatClientFactory(
    IModelProfileResolver resolver,
    AiProviderRegistry providers,
    IAiOptionsSource optionsSource,
    ILoggerFactory loggerFactory,
    IProviderErrorLog? providerErrors = null) : IChatClientFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IChatClient>> _clients = new(StringComparer.Ordinal);
    private readonly ILogger _logger = loggerFactory.CreateLogger<ChatClientFactory>();

    public async Task<ChatModelHandle> CreateAsync(
        string profileName,
        ModelProfilePatch? promptPatch = null,
        ModelProfilePatch? requestPatch = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await resolver.ResolveAsync(profileName, promptPatch, requestPatch, cancellationToken);
        // Rerank profiles are allowed: on a chat provider they rerank with an LLM.
        if (profile.Kind == ModelKind.Embedding)
        {
            throw new ModelNotConfiguredException(
                $"The '{profile.Name}' profile is an embedding profile and cannot be used for chat; choose a chat profile.");
        }

        var options = await optionsSource.GetCurrentAsync(cancellationToken);
        if (!options.Providers.TryGetValue(profile.ProviderKey, out var providerOptions) || providers.Find(providerOptions.Type) is not { } provider)
        {
            throw new ModelNotConfiguredException(
                $"Provider '{profile.ProviderKey}' of the '{profile.Name}' profile is no longer available; check AI:Providers.");
        }

        var sensitiveData = options.Telemetry.EnableSensitiveData;
        var client = GetOrCreateClient(provider, providerOptions, profile, sensitiveData);
        return new ChatModelHandle(client, profile, () => CreateOptions(profile, provider, providerOptions));
    }

    internal static ChatOptions CreateOptions(ResolvedModelProfile profile, IAiProvider provider, AiProviderOptions providerOptions)
    {
        var options = new ChatOptions
        {
            ModelId = profile.Model,
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
            Reasoning = ReasoningSettings.ToReasoningOptions(profile.ReasoningEffort, profile.ReasoningOutput),
        };

        // Applied last so providers can drop settings their models reject.
        provider.ConfigureChatOptions(options, providerOptions, profile);
        return options;
    }

    private IChatClient GetOrCreateClient(IAiProvider provider, AiProviderOptions providerOptions, ResolvedModelProfile profile, bool sensitiveData)
    {
        var key = ClientCacheKey.Create(profile.ProviderKey, provider.Type, profile.Model, providerOptions, sensitiveData ? "sensitive" : "redacted");
        var lazy = _clients.GetOrAdd(key, _ => new Lazy<IChatClient>(() => Build(provider, providerOptions, profile, sensitiveData)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Failures are not cached so a corrected configuration or a transient error can succeed later.
            _clients.TryRemove(new KeyValuePair<string, Lazy<IChatClient>>(key, lazy));
            throw;
        }
    }

    private IChatClient Build(IAiProvider provider, AiProviderOptions providerOptions, ResolvedModelProfile profile, bool sensitiveData)
    {
        IChatClient inner;
        try
        {
            inner = provider.CreateChatClient(providerOptions, profile);
        }
        catch (Exception ex) when (ex is not ModelNotConfiguredException and not OperationCanceledException)
        {
            LogCreateFailed(_logger, ex, profile.ProviderKey, profile.Model);
            throw new ModelNotConfiguredException(
                $"Could not create a chat client for provider '{profile.ProviderKey}' ({profile.Model}): {ex.Message} "
                + $"Check AI:Providers:{profile.ProviderKey} or switch the '{profile.Name}' profile in Models.");
        }

        LogClientCreated(_logger, profile.ProviderKey, provider.Type, profile.Model);

        // No function-invocation stage on purpose: callers run their own tool loop.
        return new ChatClientBuilder(inner)
            .UseOpenTelemetry(loggerFactory, KnowledgeAgentTelemetry.AI, client => client.EnableSensitiveData = sensitiveData)
            .UseLogging(loggerFactory)
            .Use(static innerClient => new PromptTelemetryChatClient(innerClient))
            .Use(innerClient => providerErrors is null ? innerClient : new ProviderErrorChatClient(innerClient, providerErrors, profile.ProviderKey))
            .Build();
    }

    public void Dispose()
    {
        foreach (var lazy in _clients.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        _clients.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created chat client for provider {Provider} ({ProviderType}), model {Model}")]
    private static partial void LogClientCreated(ILogger logger, string provider, string providerType, string model);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating the chat client for provider {Provider}, model {Model} failed")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string provider, string model);
}
