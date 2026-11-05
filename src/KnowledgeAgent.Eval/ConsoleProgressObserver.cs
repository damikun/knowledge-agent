using System.Globalization;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Running;

namespace KnowledgeAgent.Eval;

// Progress goes to stderr so that stdout carries only the report.
internal sealed class ConsoleProgressObserver(bool quiet) : IEvalRunObserver
{
    private readonly Lock _gate = new();
    private int _total;
    private int _done;

    public Task OnRunStartedAsync(EvalRunPlan plan, EvalRunSettings settings, int totalItems, CancellationToken cancellationToken)
    {
        _total = totalItems;
        if (!quiet)
        {
            Console.Error.WriteLine(
                $"Running {totalItems} items · agent {settings.ChatModelLabel} · embeddings {settings.EmbeddingModel} · prompt {settings.PromptName} v{settings.PromptVersion} ({settings.PromptOrigin})"
                + (settings.JudgeModel is null ? string.Empty : $" · judge {settings.JudgeModel}"));
        }

        return Task.CompletedTask;
    }

    public Task OnItemCompletedAsync(EvalRunPlan plan, EvalItemOutcome outcome, CancellationToken cancellationToken)
    {
        if (quiet)
        {
            return Task.CompletedTask;
        }

        var item = outcome.Result;
        lock (_gate)
        {
            _done++;
            var shown = new[] { EvalMetrics.RetrievalRecallAt5, EvalMetrics.CitationSourceRecall, EvalMetrics.AnswerFactRecall, EvalMetrics.AnswerAbstained, EvalMetrics.JudgeCorrectness }
                .Where(item.Metrics.ContainsKey)
                .Select(metric => $"{Short(metric)} {item.Metrics[metric].ToString("0%", CultureInfo.InvariantCulture)}");
            var status = item.Status == EvalItemStatuses.Failed ? "FAIL" : "ok  ";
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[{_done,3}/{_total}] {status} {item.ItemId,-40} {string.Join("  ", shown)}  ({item.DurationMs / 1000.0:0.0} s){(item.Error is null ? string.Empty : "  " + item.Error)}"));
        }

        return Task.CompletedTask;
    }

    private static string Short(string metric) => metric switch
    {
        EvalMetrics.RetrievalRecallAt5 => "recall@5",
        EvalMetrics.CitationSourceRecall => "cited",
        EvalMetrics.AnswerFactRecall => "facts",
        EvalMetrics.AnswerAbstained => "declined",
        EvalMetrics.JudgeCorrectness => "correct",
        _ => metric,
    };
}
