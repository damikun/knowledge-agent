using global::Anthropic;
using global::Anthropic.Core;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;

internal sealed class AnthropicAiProvider(ILogger<AnthropicAiProvider> logger) : IAiProvider, IAiProviderDescriptor
{
    // Thinking tokens count toward this limit.
    public const int DefaultMaxOutputTokens = 16000;

    private readonly OneTimeWarnings _warnings = new(logger);

    public string Type => ProviderTypes.Anthropic;

    public bool SupportsChat => true;

    public bool SupportsEmbeddings => false;

    public bool UsesApiKey => true;

    public bool UsesEndpoint => true;

    public bool IsConfigured(AiProviderOptions options) => !string.IsNullOrWhiteSpace(options.ApiKey);

    public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile)
    {
        if (!IsConfigured(options))
        {
            throw new ModelNotConfiguredException(
                $"Provider '{profile.ProviderKey}' is not configured: {DescribeMissingConfiguration(profile.ProviderKey, options)}.");
        }

        var settings = AnthropicSettings.From(options);
        var client = CreateAnthropicClient(options, settings);
        var adapter = settings.UsesBetaEndpoint
            ? client.Beta.AsIChatClient(profile.Model, DefaultMaxOutputTokens, settings.ThinkingMode)
            : client.AsIChatClient(profile.Model, DefaultMaxOutputTokens, settings.ThinkingMode);

        return new ClaudeChatClient(adapter, settings, profile.Model, DefaultMaxOutputTokens, _warnings, client as IDisposable);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
        throw new NotSupportedException("Anthropic does not offer an embeddings API; use voyage, openai or ollama for the embedding profile.");

    public void ConfigureChatOptions(ChatOptions chatOptions, AiProviderOptions options, ResolvedModelProfile profile)
    {
        AnthropicRequestRules.Apply(chatOptions, AnthropicSettings.From(options).ThinkingMode, defaultEffort: true, _warnings);
    }

    public async Task<ProviderConnectionTest> TestConnectionAsync(AiProviderOptions options, CancellationToken cancellationToken = default)
    {
        var client = CreateAnthropicClient(options, AnthropicSettings.From(options));
        using var disposable = client as IDisposable;
        var page = await client.Models.List(cancellationToken: cancellationToken);
        var models = page.Items.Select(model => model.ID).Order(StringComparer.Ordinal).ToList();
        return new ProviderConnectionTest(true, $"Connected to Anthropic; {models.Count} models available.", models);
    }

    public string DescribeMissingConfiguration(string providerKey, AiProviderOptions options) =>
        $"add an API key under Models → Providers, or set AI__Providers__{providerKey}__ApiKey"
        + (AiOptionsPostConfigure.AllowsApiKeyFallback(providerKey, options) ? $" (or {FallbackConfigurationKeys.AnthropicApiKey})" : string.Empty);

    internal static AnthropicClient CreateAnthropicClient(AiProviderOptions options, AnthropicSettings settings) => new()
    {
        ApiKey = options.ApiKey!.Trim(),

        // Always explicit so that an ambient ANTHROPIC_BASE_URL on the machine never redirects the application.
        BaseUrl = string.IsNullOrWhiteSpace(options.Endpoint) ? EnvironmentUrl.Production : options.Endpoint.Trim(),
        MaxRetries = settings.MaxRetries,
        Timeout = settings.Timeout,

        // The Microsoft.Extensions.AI pipeline already emits the GenAI spans.
        OpenTelemetry = false,
    };
}
