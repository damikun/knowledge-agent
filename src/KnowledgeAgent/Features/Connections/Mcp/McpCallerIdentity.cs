using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.People;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Connections.Mcp;

internal static class McpCallerIdentity
{
    public const string MetaKey = "knowledge-agent/caller";

    public static async Task<JsonObject> ForAsync(ChatCaller caller, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var identity = new JsonObject
        {
            ["channel"] = caller.Channel.ToString().ToLowerInvariant(),
            ["verified"] = false,
        };

        if (caller is { Channel: ConversationChannel.Widget, Verified: true, ContactId: { } contactId }
            && services.GetService<IPeopleRepository>() is { } people
            && await people.GetAsync(contactId, cancellationToken) is { ExternalId: { Length: > 0 } userId })
        {
            identity["verified"] = true;
            identity["userId"] = userId;
        }

        return identity;
    }
}
