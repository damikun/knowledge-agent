using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI;

public interface IAiProvider
{
    string Type { get; }

    bool SupportsChat { get; }
    bool SupportsEmbeddings { get; }

    bool IsConfigured(AiProviderOptions options);

    IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile);

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile);

    void ConfigureChatOptions(ChatOptions chatOptions, AiProviderOptions options, ResolvedModelProfile profile)
    {
    }

    void ConfigureEmbeddingOptions(EmbeddingGenerationOptions embeddingOptions, AiProviderOptions options, ResolvedModelProfile profile, EmbeddingPurpose purpose)
    {
    }

    bool SupportsRerank => false;

    IReranker CreateReranker(AiProviderOptions options, ResolvedModelProfile profile) =>
        throw new NotSupportedException($"Provider type '{Type}' has no dedicated reranker.");

    bool UsesApiKey => false;

    bool UsesEndpoint => false;

    bool EndpointRequired => false;

    Task<ProviderConnectionTest> TestConnectionAsync(AiProviderOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderConnectionTest(true, "This provider needs no connection.", []));
}

public static class AiProviderCapabilities
{
    public static bool Supports(this IAiProvider provider, ModelKind kind) => kind switch
    {
        ModelKind.Embedding => provider.SupportsEmbeddings,
        ModelKind.Rerank => provider.SupportsRerank || provider.SupportsChat,
        _ => provider.SupportsChat,
    };
}

public sealed record ProviderConnectionTest(bool Ok, string Message, IReadOnlyList<string> Models);

public interface IReranker
{
    Task<IReadOnlyList<double>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default);
}
