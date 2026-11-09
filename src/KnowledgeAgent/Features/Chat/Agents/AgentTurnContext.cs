using System.Collections.Concurrent;
using System.Threading.Channels;
using KnowledgeAgent.Features.Chat.Tools;

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

    // Shared with sub-agents, which run on the same turn.
    public string UntrustedKey { get; } = UntrustedText.NewKey();

    private readonly ConcurrentDictionary<string, int> _budget = new(StringComparer.Ordinal);

    private AgentTransfer? _transfer;

    private int _steps;

    // Model calls are numbered across the turn, so a hand-off or a sub-agent never reuses a number.
    public int NextStep() => Interlocked.Increment(ref _steps);

    // Tools run in parallel, so the count is taken atomically.
    public bool TryTakeBudget(string kind, int max)
    {
        while (true)
        {
            var used = _budget.GetOrAdd(kind, 0);
            if (used >= max)
            {
                return false;
            }

            if (_budget.TryUpdate(kind, used + 1, used))
            {
                return true;
            }
        }
    }

    public int BudgetUsed(string kind) => _budget.GetValueOrDefault(kind);

    public bool RequestTransfer(AgentTransfer transfer) => Interlocked.CompareExchange(ref _transfer, transfer, null) is null;

    public AgentTransfer? TakeTransfer() => Interlocked.Exchange(ref _transfer, null);

    public bool IsInScope(Guid sourceId) => ScopeSourceIds is null || ScopeSourceIds.Contains(sourceId);
}

internal sealed record AgentTransfer(KnowledgeAgent.Features.Agents.AgentConfig Target, string Reason, string CallId, bool Back);
