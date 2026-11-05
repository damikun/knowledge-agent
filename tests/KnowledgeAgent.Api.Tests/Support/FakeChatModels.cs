using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Api.Tests.Support;

// Each chat profile gets its own model because the app caches chat clients per provider and model.
public static class FakeModels
{
    public const string ProviderKey = "fake";

    public const string ChatModel = "fake-chat";

    public const string UtilityModel = "fake-utility";
    public const string ResearcherModel = "fake-researcher";
    public const string EmbeddingModel = "fake-embedding";
    public const int EmbeddingDimensions = 64;
}

public sealed class FakeChatModels
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IChatClient> _byProfile = new(StringComparer.OrdinalIgnoreCase);
    private IChatClient _fallback = new RuleBasedChatClient();

    public RuleBasedChatClient Reset()
    {
        var client = new RuleBasedChatClient();
        lock (_gate)
        {
            _byProfile.Clear();
            _fallback = client;
        }

        return client;
    }

    public T Use<T>(string profileName, T client) where T : IChatClient
    {
        lock (_gate)
        {
            _byProfile[profileName] = client;
        }

        return client;
    }

    public IChatClient Resolve(string profileName)
    {
        lock (_gate)
        {
            return _byProfile.TryGetValue(profileName, out var client) ? client : _fallback;
        }
    }
}

// Resolves the current fake on every call, so tests can swap clients after the app cached this one.
public sealed class SwitchableChatClient(FakeChatModels models, ResolvedModelProfile profile) : IChatClient
{
    public ResolvedModelProfile Profile { get; } = profile;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        models.Resolve(Profile.Name).GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        models.Resolve(Profile.Name).GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : models.Resolve(Profile.Name).GetService(serviceType, serviceKey);

    public void Dispose()
    {
    }
}
