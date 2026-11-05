using System.ClientModel;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace KnowledgeAgent.Infrastructure.AI.Providers.OpenAI;

internal sealed class OpenAiProvider(ILogger<OpenAiProvider> logger) : IAiProvider, IAiProviderDescriptor
{
    // The SDK requires a non-empty key, even for keyless OpenAI-compatible servers.
    internal const string KeylessCredential = "no-key";

    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(10);

    private readonly OneTimeWarnings _warnings = new(logger);

    public string Type => ProviderTypes.OpenAI;

    public bool SupportsChat => true;

    public bool SupportsEmbeddings => true;

    public bool UsesApiKey => true;

    public bool UsesEndpoint => true;

    public bool IsConfigured(AiProviderOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey) || IsValidEndpoint(options.Endpoint);

    public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile) =>
        CreateClient(options).GetChatClient(profile.Model).AsIChatClient();

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
        CreateClient(options).GetEmbeddingClient(profile.Model).AsIEmbeddingGenerator(profile.Dimensions);

    public void ConfigureChatOptions(ChatOptions chatOptions, AiProviderOptions options, ResolvedModelProfile profile)
    {
        var model = chatOptions.ModelId ?? profile.Model;
        if (IsReasoningModel(model))
        {
            if (chatOptions.Temperature is not null)
            {
                _warnings.Warn($"openai.temperature.{model}", $"OpenAI reasoning model '{model}' only supports the default temperature; the configured temperature is not sent.");
                chatOptions.Temperature = null;
            }

            if (chatOptions.Reasoning is { } reasoning)
            {
                // Chat completions have no reasoning summaries, so only the effort is kept.
                chatOptions.Reasoning = reasoning.Effort is null ? null : new ReasoningOptions { Effort = reasoning.Effort };
            }
        }
        else
        {
            // Non-reasoning models reject reasoning parameters.
            chatOptions.Reasoning = null;
        }
    }

    public async Task<ProviderConnectionTest> TestConnectionAsync(AiProviderOptions options, CancellationToken cancellationToken = default)
    {
        var result = await CreateClient(options).GetOpenAIModelClient().GetModelsAsync(cancellationToken);
        var models = result.Value.Select(model => model.Id);

        // No endpoint means the official API, which also lists models that are neither chat nor embedding models.
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            models = models.Where(IsChatOrEmbeddingModel);
        }

        var list = models.Order(StringComparer.Ordinal).ToList();
        return new ProviderConnectionTest(true, $"Connected; {list.Count} models available.", list);
    }

    internal static bool IsChatOrEmbeddingModel(string model) =>
        model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("text-embedding-", StringComparison.OrdinalIgnoreCase)
        || (model.Length > 1 && (model[0] is 'o' or 'O') && char.IsDigit(model[1]));

    public string DescribeMissingConfiguration(string providerKey, AiProviderOptions options) =>
        $"add an API key under Models → Providers, or set AI__Providers__{providerKey}__ApiKey"
        + (AiOptionsPostConfigure.AllowsApiKeyFallback(providerKey, options) ? $" (or {FallbackConfigurationKeys.OpenAIApiKey})" : string.Empty)
        + $" or AI__Providers__{providerKey}__Endpoint for an OpenAI-compatible server";

    internal static bool IsReasoningModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var name = model.Trim().ToLowerInvariant();
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.StartsWith("gpt-5", StringComparison.Ordinal))
        {
            return !name.Contains("-chat", StringComparison.Ordinal);
        }

        return name.Length > 1 && name[0] == 'o' && char.IsDigit(name[1]);
    }

    internal static OpenAIClient CreateClient(AiProviderOptions options)
    {
        var clientOptions = new OpenAIClientOptions { NetworkTimeout = NetworkTimeout };
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(options.Endpoint.Trim(), UriKind.Absolute);
        }

        var key = string.IsNullOrWhiteSpace(options.ApiKey) ? KeylessCredential : options.ApiKey.Trim();
        return new OpenAIClient(new ApiKeyCredential(key), clientOptions);
    }

    private static bool IsValidEndpoint(string? endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint)
        && Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
