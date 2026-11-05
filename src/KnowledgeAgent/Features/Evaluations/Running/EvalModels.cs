using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Features.Evaluations.Running;

public sealed record EvalRunPlan(
    Guid RunId,
    string Name,
    EvalDataset Dataset,
    bool Judge,
    string? Profile,
    int Parallelism,
    int TopK,
    EvalExperimentIdentity? Experiment = null);

public sealed record EvalExperimentIdentity(
    string Id,
    string Name,
    string? DatasetId,
    string? Description,
    string Environment,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record AgentTurnResult(
    string Answer,
    IReadOnlyDictionary<string, CitationDto> Available,
    string? FinishReason,
    string? Error,
    string? ErrorCode,
    string? Provider,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    int ToolCalls,
    Guid? ConversationId,
    string? TraceId,
    string? RootSpanId,
    long DurationMs)
{
    public bool Failed => Error is not null;
}

public sealed class ResolvedSources(IReadOnlyDictionary<string, IReadOnlySet<Guid>> idsByReference)
{
    private static readonly IReadOnlySet<Guid> None = new HashSet<Guid>();

    public IReadOnlySet<Guid> this[string reference] =>
        idsByReference.TryGetValue(reference, out var ids) ? ids : None;

    public IEnumerable<string> Unresolved => idsByReference.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key);
}

public sealed record EvalRunSettings(
    string Target,
    string Profile,
    string? ChatProvider,
    string? ChatModel,
    string? EmbeddingModel,
    string? JudgeModel,
    string? PromptName,
    int? PromptVersion,
    string? PromptOrigin,
    int Parallelism,
    int TopK,
    string SearchMode = SearchModes.Hybrid,
    string? RerankModel = null)
{
    public string? ChatModelLabel => ChatProvider is null || ChatModel is null ? null : $"{ChatProvider}/{ChatModel}";
}

public sealed record EvalRunResult(
    EvalRunPlan Plan,
    EvalRunSettings Settings,
    IReadOnlyList<EvalItemResultDto> Items,
    IReadOnlyDictionary<string, EvalMetricValueDto> Metrics,
    IReadOnlyList<EvalTagSummaryDto> ByTag,
    IReadOnlyList<string> Warnings,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool Cancelled)
{
    public int FailedItems => Items.Count(item => item.Status == EvalItemStatuses.Failed);
}

public static class EvalItemStatuses
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public interface IEvalRunObserver
{
    Task OnRunStartedAsync(EvalRunPlan plan, EvalRunSettings settings, int totalItems, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called once per item, possibly concurrently for different items.</summary>
    Task OnItemCompletedAsync(EvalRunPlan plan, EvalItemOutcome outcome, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnRunCompletedAsync(EvalRunResult result, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IEvalTarget : IAsyncDisposable
{
    string Kind { get; }

    IServiceProvider Services { get; }

    Task<ResolvedSources> ResolveSourcesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken);

    Task CleanupConversationAsync(Guid conversationId, CancellationToken cancellationToken);
}

public sealed record EvalItemOutcome(EvalItemResultDto Result, string? RootSpanId);

public sealed record RetrievalResult(IReadOnlyList<KnowledgeSearchHit> Hits, string? Error);
