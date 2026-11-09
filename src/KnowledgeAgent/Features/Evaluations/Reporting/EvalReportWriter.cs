using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Gate;
using KnowledgeAgent.Features.Evaluations.Running;

namespace KnowledgeAgent.Features.Evaluations.Reporting;

public static class EvalReportWriter
{
    internal const int WeakestItems = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Json(EvalRunDto run) => JsonSerializer.Serialize(run, JsonOptions);

    public static string Markdown(EvalRunDto run, EvalBaseline? baseline)
    {
        var summary = run.Summary;
        var config = run.Config;
        var gate = summary.Gate;
        var builder = new StringBuilder();
        var verdict = gate is null ? string.Empty : gate.Passed ? " (gate passed)" : " (gate failed)";
        builder.Append(CultureInfo.InvariantCulture, $"# Eval: {summary.Dataset}{verdict}\n\n");

        builder.Append("| | |\n|---|---|\n");
        Row(builder, "Run", $"{Escape(summary.Name)} (`{summary.Id}`)");
        Row(builder, "Status", summary.Status);
        Row(builder, "Target", config.Target == EvalTargets.Isolated ? "isolated knowledge base (builtin corpus)" : "live knowledge base");
        Row(builder, "Agent model", $"{config.ChatModel ?? "?"} (profile {config.Profile})");
        Row(builder, "Embeddings", config.EmbeddingModel ?? "?");
        Row(builder, "Search", (config.SearchMode ?? "?") + (config.RerankModel is null ? ", not reranked" : $", reranked by {config.RerankModel}"));
        Row(builder, "Prompt", config.PromptName is null ? "?" : $"{config.PromptName} v{config.PromptVersion?.ToString(CultureInfo.InvariantCulture) ?? "?"} ({config.PromptOrigin ?? "?"})");
        Row(builder, "Judge", config.JudgeModel ?? "off");
        var duration = summary.CompletedAt is { } completed && summary.StartedAt is { } started ? (completed - started).TotalSeconds : 0;
        Row(builder, "Items", string.Create(CultureInfo.InvariantCulture, $"{summary.CompletedItems} of {summary.TotalItems} ({summary.FailedItems} failed) in {duration:0.0} s"));
        if (gate?.BaselineSignature is { } signature)
        {
            Row(builder, "Baseline", $"`{signature}`");
        }

        if (summary.LangfuseUrl is { } url)
        {
            Row(builder, "Langfuse", $"[experiment]({url})");
        }

        builder.Append("\n## Metrics\n\n| Metric | Mean | Items | Baseline | Change |\n|---|---:|---:|---:|---:|\n");
        foreach (var metric in EvalMetrics.All.Where(summary.Metrics.ContainsKey))
        {
            var value = summary.Metrics[metric];
            var baselineValue = baseline?.Metrics.TryGetValue(metric, out var accepted) == true ? accepted : (double?)null;
            builder.Append(CultureInfo.InvariantCulture, $"| {EvalMetricLabels.Of(metric)} | {QualityGate.Percent(value.Mean)} | {value.Count} | ");
            builder.Append(baselineValue is { } b ? QualityGate.Percent(b) : "-").Append(" | ");
            builder.Append(baselineValue is { } before ? QualityGate.Points(value.Mean - before) : "-").Append(" |\n");
        }

        if (gate is not null)
        {
            builder.Append("\n## Gate\n\n");
            foreach (var check in gate.Checks)
            {
                builder.Append(check.Passed ? "- PASS: " : "- FAIL: ").Append(Escape(check.Message)).Append('\n');
            }
        }

        if (run.ByTag.Count > 0)
        {
            string[] columns = [EvalMetrics.RetrievalRecallAt5, EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationSourceRecall, EvalMetrics.AnswerFactRecall, EvalMetrics.AnswerAbstained, EvalMetrics.JudgeCorrectness];
            var shown = columns.Where(metric => run.ByTag.Any(tag => tag.Metrics.ContainsKey(metric))).ToList();
            builder.Append("\n## By tag\n\n| Tag | Items | ").Append(string.Join(" | ", shown.Select(EvalMetricLabels.Of))).Append(" |\n|---|---:|");
            builder.Append(string.Concat(shown.Select(_ => "---:|"))).Append('\n');
            foreach (var tag in run.ByTag)
            {
                builder.Append(CultureInfo.InvariantCulture, $"| {Escape(tag.Tag)} | {tag.Items} |");
                foreach (var metric in shown)
                {
                    builder.Append(' ').Append(tag.Metrics.TryGetValue(metric, out var value) ? QualityGate.Percent(value.Mean) : "-").Append(" |");
                }

                builder.Append('\n');
            }
        }

        var weakest = run.Items
            .Select(item => (item, score: Weakness(item)))
            .Where(entry => entry.score < 1)
            .OrderBy(entry => entry.score)
            .ThenBy(entry => entry.item.ItemId, StringComparer.Ordinal)
            .Take(WeakestItems)
            .ToList();
        if (weakest.Count > 0)
        {
            builder.Append("\n## Weakest items\n\n| Item | Question | Problems |\n|---|---|---|\n");
            foreach (var (item, _) in weakest)
            {
                builder.Append(CultureInfo.InvariantCulture, $"| `{item.ItemId}` | {Escape(Shorten(item.Question, 90))} | {Escape(Problems(item))} |\n");
            }
        }

        if (run.Warnings.Count > 0)
        {
            builder.Append("\n## Warnings\n\n");
            foreach (var warning in run.Warnings)
            {
                builder.Append("- ").Append(Escape(warning)).Append('\n');
            }
        }

        return builder.ToString();
    }

    internal static double Weakness(EvalItemResultDto item)
    {
        if (item.Status == EvalItemStatuses.Failed)
        {
            return -1;
        }

        string[] keys =
        [
            EvalMetrics.AnswerFactRecall, EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationSourceRecall, EvalMetrics.CitationValidity,
            EvalMetrics.AnswerAbstained, EvalMetrics.JudgeCorrectness, EvalMetrics.JudgeFaithfulness,
        ];
        var values = keys.Where(item.Metrics.ContainsKey).Select(key => item.Metrics[key]).ToList();
        return values.Count == 0 ? 1 : values.Min();
    }

    internal static string Problems(EvalItemResultDto item)
    {
        if (item.Status == EvalItemStatuses.Failed)
        {
            return "failed: " + (item.Error ?? "unknown error");
        }

        var problems = new List<string>();
        if (item.MissingFacts.Count > 0)
        {
            problems.Add("missing facts: " + string.Join(", ", item.MissingFacts.Select(fact => fact.Split('|')[0])));
        }

        if (item.Metrics.TryGetValue(EvalMetrics.AnsweredWithCitation, out var answered) && answered < 1)
        {
            problems.Add("answered without a valid citation");
        }

        if (item.Metrics.TryGetValue(EvalMetrics.CitationSourceRecall, out var cited) && cited < 1)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"expected sources cited {cited:0%}"));
        }

        if (item.Citations.Any(citation => !citation.Valid))
        {
            problems.Add("unknown citation tags: " + string.Join(", ", item.Citations.Where(citation => !citation.Valid).Select(citation => citation.Ref)));
        }

        if (item.Metrics.TryGetValue(EvalMetrics.AnswerAbstained, out var abstained) && abstained < 1)
        {
            problems.Add("did not decline an unanswerable question");
        }

        foreach (var criterion in item.Judgement?.Criteria.Where(criterion => criterion.Score < 1) ?? [])
        {
            problems.Add($"judge {criterion.Name}: {criterion.Verdict}");
        }

        return problems.Count == 0 ? "none" : string.Join("; ", problems);
    }

    private static void Row(StringBuilder builder, string name, string value) => builder.Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    private static string Escape(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
}
