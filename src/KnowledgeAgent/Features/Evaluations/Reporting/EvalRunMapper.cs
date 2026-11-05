using KnowledgeAgent.Features.Evaluations.Running;

namespace KnowledgeAgent.Features.Evaluations.Reporting;

public static class EvalRunMapper
{
    public static EvalRunConfigDto ToConfig(EvalRunSettings settings, int? maxItems, IReadOnlyList<string> tags) => new(
        settings.Target,
        settings.Profile,
        settings.ChatModelLabel,
        settings.EmbeddingModel,
        settings.JudgeModel,
        settings.PromptName,
        settings.PromptVersion,
        settings.PromptOrigin,
        settings.Parallelism,
        settings.TopK,
        maxItems,
        tags,
        settings.SearchMode,
        settings.RerankModel);

    public static EvalRunDto ToDto(
        EvalRunResult result,
        string status,
        EvalGateDto? gate,
        string? langfuseUrl,
        int? maxItems,
        IReadOnlyList<string> tags,
        IReadOnlyList<string>? extraWarnings = null,
        string? error = null) => new(
        new EvalRunSummaryDto(
            result.Plan.RunId,
            result.Plan.Name,
            result.Plan.Dataset.Name,
            result.Plan.Dataset.Source,
            status,
            result.Plan.Judge,
            result.Plan.Dataset.Items.Count,
            result.Items.Count,
            result.FailedItems,
            result.Settings.ChatModelLabel,
            result.Settings.EmbeddingModel,
            result.Metrics,
            gate,
            error,
            langfuseUrl,
            result.StartedAt,
            result.StartedAt,
            result.CompletedAt),
        ToConfig(result.Settings, maxItems, tags),
        result.ByTag,
        result.Items,
        result.Warnings.Concat(extraWarnings ?? []).ToList());
}
