using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Chat;

public sealed record ChatModelHandle(IChatClient Client, ResolvedModelProfile Profile, Func<ChatOptions> CreateOptions);

public interface IChatClientFactory
{
    Task<ChatModelHandle> CreateAsync(
        string profileName,
        ModelProfilePatch? promptPatch = null,
        ModelProfilePatch? requestPatch = null,
        CancellationToken cancellationToken = default);
}
