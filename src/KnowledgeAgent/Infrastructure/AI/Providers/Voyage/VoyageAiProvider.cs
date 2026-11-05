using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Voyage;

internal sealed class VoyageAiProvider : IAiProvider, IAiProviderDescriptor
{
    public const string DefaultEndpoint = "https://api.voyageai.com/v1/";
    public const string DefaultRerankModel = "rerank-2.5";
    public const string DefaultEmbeddingModel = "voyage-4";
    public const int DefaultDimensions = 1024;

    // Voyage has no model listing endpoint, so the connection test reports this fixed list.
    internal static readonly IReadOnlyList<string> KnownModels =
        ["rerank-2.5", "rerank-2.5-lite", "voyage-3.5", "voyage-3.5-lite", "voyage-4", "voyage-4-large", "voyage-4-lite", "voyage-code-3"];

    public string Type => ProviderTypes.Voyage;

    public bool SupportsChat => false;

    public bool SupportsEmbeddings => true;

    public bool SupportsRerank => true;

    public bool UsesApiKey => true;

    public bool UsesEndpoint => true;

    public bool IsConfigured(AiProviderOptions options) => !string.IsNullOrWhiteSpace(options.ApiKey) && TryGetEndpoint(options, out _);

    public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile) =>
        throw new NotSupportedException("Voyage AI has no chat models.");

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
        new VoyageEmbeddingGenerator(CreateClient(options), Endpoint(options), profile.Model, profile.Dimensions);

    public IReranker CreateReranker(AiProviderOptions options, ResolvedModelProfile profile) =>
        new VoyageReranker(CreateClient(options), profile.Model);

    public void ConfigureEmbeddingOptions(EmbeddingGenerationOptions embeddingOptions, AiProviderOptions options, ResolvedModelProfile profile, EmbeddingPurpose purpose)
    {
        embeddingOptions.AdditionalProperties ??= [];
        embeddingOptions.AdditionalProperties[VoyageEmbeddingGenerator.InputTypeProperty] = purpose == EmbeddingPurpose.Query ? "query" : "document";
    }

    // A rerank request of a few tokens is the cheapest way to verify the key.
    public async Task<ProviderConnectionTest> TestConnectionAsync(AiProviderOptions options, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        var model = string.IsNullOrWhiteSpace(options.DefaultRerankModel) ? DefaultRerankModel : options.DefaultRerankModel.Trim();
        await client.RerankAsync(model, "connection test", ["connection test"], cancellationToken);
        return new ProviderConnectionTest(true, $"Connected; reranking with {model} works.", KnownModels);
    }

    public string DescribeMissingConfiguration(string providerKey, AiProviderOptions options) =>
        $"add an API key under Models → Providers, or set AI__Providers__{providerKey}__ApiKey (or {FallbackConfigurationKeys.VoyageApiKey})";

    public string? GetBuiltInDefaultModel(ModelKind kind) => kind switch
    {
        ModelKind.Embedding => DefaultEmbeddingModel,
        ModelKind.Rerank => DefaultRerankModel,
        _ => null,
    };

    public int? GetBuiltInDefaultDimensions() => DefaultDimensions;

    internal static VoyageClient CreateClient(AiProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("The Voyage API key is missing.");
        }

        return new VoyageClient(Endpoint(options), options.ApiKey);
    }

    private static Uri Endpoint(AiProviderOptions options) =>
        TryGetEndpoint(options, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException("The Voyage endpoint is not an absolute http(s) URL.");

    private static bool TryGetEndpoint(AiProviderOptions options, out Uri endpoint)
    {
        var value = string.IsNullOrWhiteSpace(options.Endpoint) ? DefaultEndpoint : options.Endpoint.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            endpoint = uri;
            return true;
        }

        endpoint = null!;
        return false;
    }
}
