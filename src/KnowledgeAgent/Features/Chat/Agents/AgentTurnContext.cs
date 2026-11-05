using System.Threading.Channels;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class AgentTurnContext(
    Guid conversationId,
    Guid turnId,
    IReadOnlyList<Guid>? scopeSourceIds,
    CitationRegistry citations,
    ChannelWriter<ChatStreamEvent> events)
{
    public Guid ConversationId { get; } = conversationId;
    public Guid TurnId { get; } = turnId;

    // Null means every source; empty means none (folders that hold no sources yet).
    public IReadOnlyList<Guid>? ScopeSourceIds { get; set; } = scopeSourceIds;

    public bool HasNoSources => ScopeSourceIds is { Count: 0 };

    public CitationRegistry Citations { get; } = citations;
    public ChannelWriter<ChatStreamEvent> Events { get; } = events;
    public ToolCallLog ToolCalls { get; } = new();
    public UsageAccumulator Usage { get; } = new();

    public ChatCaller Caller { get; init; } = ChatCaller.Portal;

    private AgentTransfer? _transfer;

    public bool RequestTransfer(AgentTransfer transfer) => Interlocked.CompareExchange(ref _transfer, transfer, null) is null;

    public AgentTransfer? TakeTransfer() => Interlocked.Exchange(ref _transfer, null);

    public bool IsInScope(Guid sourceId) => ScopeSourceIds is null || ScopeSourceIds.Contains(sourceId);
}

internal sealed record AgentTransfer(KnowledgeAgent.Features.Agents.AgentConfig Target, string Reason, string CallId, bool Back);
