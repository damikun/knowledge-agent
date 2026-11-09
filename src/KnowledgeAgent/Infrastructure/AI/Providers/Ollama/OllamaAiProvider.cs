using System.Globalization;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Ollama;

internal sealed class OllamaAiProvider : IAiProvider, IAiProviderDescriptor
{
    // Local inference can be slow, and the HttpClient default of 100 seconds is too short for it.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    public const string NumCtxKey = "NumCtx";

    // Ollama's own default is a few thousand tokens and it drops the oldest messages beyond it without an error.
    public const int DefaultNumCtx = 32768;

    public string Type => ProviderTypes.Ollama;

    public bool SupportsChat => true;

    public bool SupportsEmbeddings => true;

    public bool UsesEndpoint => true;

    public bool EndpointRequired => true;

    public bool IsConfigured(AiProviderOptions options) => TryGetEndpoint(options, out _);

    public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile) => CreateClient(options, profile.Model);

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
        CreateClient(options, profile.Model);

    public void ConfigureChatOptions(ChatOptions chatOptions, AiProviderOptions options, ResolvedModelProfile profile)
    {
        // Not forwarded: sending thinking settings to a local model without thinking support fails.
        chatOptions.Reasoning = null;

        if (NumCtx(options) is { } numCtx)
        {
            chatOptions.AddOllamaOption(OllamaOption.NumCtx, numCtx);
        }
    }

    // "default" or "0" leaves the context size to the Ollama server.
    internal static int? NumCtx(AiProviderOptions options)
    {
        var value = options.Settings.FirstOrDefault(entry => entry.Key.Equals(NumCtxKey, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return DefaultNumCtx;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0 ? size : null;
    }

    public async Task<ProviderConnectionTest> TestConnectionAsync(AiProviderOptions options, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options, options.DefaultChatModel ?? string.Empty);
        var models = (await client.ListLocalModelsAsync(cancellationToken)).Select(model => model.Name).Order(StringComparer.Ordinal).ToList();
        return new ProviderConnectionTest(
            true,
            models.Count == 0 ? "Connected; no models pulled yet (run `ollama pull <model>`)." : $"Connected; {models.Count} local models.",
            models);
    }

    public string DescribeMissingConfiguration(string providerKey, AiProviderOptions options) =>
        $"set the endpoint under Models → Providers, or set AI__Providers__{providerKey}__Endpoint (or {FallbackConfigurationKeys.OllamaEndpoint}) to the Ollama URL, e.g. http://localhost:11434";

    internal static OllamaApiClient CreateClient(AiProviderOptions options, string model)
    {
        if (!TryGetEndpoint(options, out var endpoint))
        {
            throw new InvalidOperationException("The Ollama endpoint is missing or not an absolute http(s) URL.");
        }

        var httpClient = new HttpClient { BaseAddress = endpoint, Timeout = RequestTimeout };
        return new OllamaApiClient(httpClient, model);
    }

    private static bool TryGetEndpoint(AiProviderOptions options, out Uri endpoint)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint)
            && Uri.TryCreate(options.Endpoint.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            endpoint = uri;
            return true;
        }

        endpoint = null!;
        return false;
    }
}
