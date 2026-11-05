using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.TestKit;

public sealed class FakeAiProvider(
    Func<ResolvedModelProfile, IChatClient>? chatClientFactory = null,
    IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null) : IAiProvider
{
    public const string TypeName = "fake";

    private readonly Func<ResolvedModelProfile, IChatClient> _chatClientFactory = chatClientFactory ?? (_ => new RuleBasedChatClient());

    public IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator { get; } = embeddingGenerator ?? new FakeEmbeddingGenerator();

    public List<ResolvedModelProfile> CreatedChatProfiles { get; } = [];

    public string Type => TypeName;

    public bool SupportsChat => true;

    public bool SupportsEmbeddings => true;

    public bool IsConfigured(AiProviderOptions options) => true;

    public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile)
    {
        lock (CreatedChatProfiles)
        {
            CreatedChatProfiles.Add(profile);
        }

        return _chatClientFactory(profile);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
        EmbeddingGenerator;
}
