namespace KnowledgeAgent.Features.Evaluations;

public static class EvalMetrics
{
    public const string RetrievalRecallAt5 = "retrieval_recall_at_5";
    public const string RetrievalRecallAt10 = "retrieval_recall_at_10";
    public const string RetrievalMrrAt10 = "retrieval_mrr_at_10";
    public const string AgentSourceRecall = "agent_source_recall";
    public const string AnsweredWithCitation = "answered_with_citation";
    public const string CitationValidity = "citation_validity";
    public const string CitationSourceRecall = "citation_source_recall";
    public const string CitationPrecision = "citation_precision";
    public const string CitationCoverage = "citation_coverage";
    public const string AnswerFactRecall = "answer_fact_recall";
    public const string AnswerAbstained = "answer_abstained";
    public const string JudgeFaithfulness = "judge_faithfulness";
    public const string JudgeRelevance = "judge_relevance";
    public const string JudgeCorrectness = "judge_correctness";

    public static readonly IReadOnlyList<string> All =
    [
        RetrievalRecallAt5,
        RetrievalRecallAt10,
        RetrievalMrrAt10,
        AgentSourceRecall,
        AnsweredWithCitation,
        CitationValidity,
        CitationSourceRecall,
        CitationPrecision,
        CitationCoverage,
        AnswerFactRecall,
        AnswerAbstained,
        JudgeFaithfulness,
        JudgeRelevance,
        JudgeCorrectness,
    ];
}

public static class EvalDatasetSources
{
    public const string Builtin = "builtin";
    public const string Langfuse = "langfuse";

    public const string Custom = "custom";

    public static bool IsKnown(string source) => source is Builtin or Langfuse or Custom;
}

public static class EvalRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsFinished(string status) => status is Completed or Failed or Cancelled;
}

public sealed record EvalDatasetDto(
    string Name,
    string Source,
    string? Description,
    int ItemCount,
    int CorpusDocuments,
    IReadOnlyList<string> Tags,
    string? LangfuseDatasetId,
    string? LangfuseUrl,
    Guid? Id = null,
    string? CopiedFrom = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record EvalDatasetListDto(
    IReadOnlyList<EvalDatasetDto> Datasets,
    bool LangfuseConfigured,
    string? LangfuseError,
    bool JudgeAvailable,
    string? JudgeModel);

public sealed record EvalDatasetSyncResultDto(
    string Dataset,
    string LangfuseDatasetId,
    int Created,
    int Updated,
    int Unchanged,
    int Archived,
    string? LangfuseUrl);

public sealed record StartEvalRunRequest(
    string Dataset,
    string? Source = null,
    bool Judge = false,
    string? Profile = null,
    int? MaxItems = null,
    IReadOnlyList<string>? Tags = null,
    string? Name = null);

public sealed record EvalMetricValueDto(double Mean, int Count);

public sealed record EvalGateDto(bool Passed, string? BaselineSignature, IReadOnlyList<EvalGateCheckDto> Checks);

public sealed record EvalGateCheckDto(string Metric, string Kind, double? Actual, double Expected, bool Passed, string Message);

public sealed record EvalRunConfigDto(
    string Target,
    string Profile,
    string? ChatModel,
    string? EmbeddingModel,
    string? JudgeModel,
    string? PromptName,
    int? PromptVersion,
    string? PromptOrigin,
    int Parallelism,
    int TopK,
    int? MaxItems,
    IReadOnlyList<string> Tags,
    string? SearchMode = null,
    string? RerankModel = null);

public sealed record EvalRunSummaryDto(
    Guid Id,
    string Name,
    string Dataset,
    string DatasetSource,
    string Status,
    bool Judge,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string? ChatModel,
    string? EmbeddingModel,
    IReadOnlyDictionary<string, EvalMetricValueDto> Metrics,
    EvalGateDto? Gate,
    string? Error,
    string? LangfuseUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record EvalRunDto(
    EvalRunSummaryDto Summary,
    EvalRunConfigDto Config,
    IReadOnlyList<EvalTagSummaryDto> ByTag,
    IReadOnlyList<EvalItemResultDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record EvalTagSummaryDto(string Tag, int Items, IReadOnlyDictionary<string, EvalMetricValueDto> Metrics);

public sealed record EvalItemResultDto(
    string ItemId,
    string Question,
    bool Answerable,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ExpectedSources,
    IReadOnlyList<string> ExpectedFacts,
    string? ReferenceAnswer,
    string Status,
    string? Answer,
    string? FinishReason,
    string? Error,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<EvalRetrievedSourceDto> Retrieved,
    IReadOnlyList<EvalCitationResultDto> Citations,
    IReadOnlyList<string> MissingFacts,
    EvalJudgementDto? Judgement,
    long DurationMs,
    long? InputTokens,
    long? OutputTokens,
    int ToolCalls,
    string? TraceId,
    string? LangfuseTraceUrl);

public sealed record EvalRetrievedSourceDto(int Rank, string Title, double Score, bool Expected);

public sealed record EvalCitationResultDto(string Ref, bool Valid, string? Title, bool Expected);

public sealed record EvalJudgementDto(string? Model, IReadOnlyList<EvalCriterionDto> Criteria, string? Error);

public sealed record EvalCriterionDto(string Name, string Verdict, double Score, string Reasoning, IReadOnlyList<string> Issues);
