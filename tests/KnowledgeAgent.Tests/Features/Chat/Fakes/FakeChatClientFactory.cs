using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

public sealed record ChatClientRequest(string Profile, ModelProfilePatch? PromptPatch, ModelProfilePatch? RequestPatch);

public sealed class FakeChatClientFactory : IChatClientFactory
{
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _notConfigured = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ChatClientRequest> _requests = new();

    public IReadOnlyList<ChatClientRequest> Requests => _requests.ToList();

    public ScriptedChatClient Script(string profile)
    {
        var client = new ScriptedChatClient();
        _clients[profile] = client;
        return client;
    }

    public T Use<T>(string profile, T client) where T : IChatClient
    {
        _clients[profile] = client;
        return client;
    }

    public void NotConfigured(string profile, string message) => _notConfigured[profile] = message;

    public Task<ChatModelHandle> CreateAsync(
        string profileName,
        ModelProfilePatch? promptPatch = null,
        ModelProfilePatch? requestPatch = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(new ChatClientRequest(profileName, promptPatch, requestPatch));

        if (_notConfigured.TryGetValue(profileName, out var reason))
        {
            throw new ModelNotConfiguredException(reason);
        }

        if (!_clients.TryGetValue(profileName, out var client))
        {
            throw new ModelNotConfiguredException($"No chat client is configured for profile '{profileName}'.");
        }

        var model = requestPatch?.Model ?? promptPatch?.Model ?? $"{profileName}-model";
        var profile = new ResolvedModelProfile
        {
            Name = profileName,
            Kind = ModelKind.Chat,
            ProviderKey = "fake",
            ProviderType = FakeAiProvider.TypeName,
            Model = model,
            ReasoningEffort = requestPatch?.ReasoningEffort ?? promptPatch?.ReasoningEffort,
            Source = requestPatch is not null ? ModelProfileSource.Request
                : promptPatch is not null ? ModelProfileSource.Prompt
                : ModelProfileSource.Configuration,
        };

        return Task.FromResult(new ChatModelHandle(client, profile, () => new ChatOptions { ModelId = model }));
    }
}
