using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Evaluations.Running;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Evaluations.Langfuse;

public sealed class LangfuseExperimentObserver(LangfuseEvalClient client, ILogger logger) : IEvalRunObserver
{
    internal const int MaxCommentLength = 500;

    private readonly ConcurrentQueue<string> _warnings = new();

    public IReadOnlyCollection<string> Warnings => _warnings.ToArray();

    public async Task OnItemCompletedAsync(EvalRunPlan plan, EvalItemOutcome outcome, CancellationToken cancellationToken)
    {
        if (plan.Experiment is not { } experiment || outcome.Result.TraceId is null || outcome.Result.Metrics.Count == 0)
        {
            return;
        }

        var item = outcome.Result;
        var scores = item.Metrics
            .Select(metric => new LangfuseScore(
                ScoreId(experiment.Id, item.ItemId, metric.Key),
                metric.Key,
                Math.Round(metric.Value, 4),
                TraceId: item.TraceId,
                ObservationId: outcome.RootSpanId,
                Comment: Comment(item, metric.Key),
                Environment: experiment.Environment,
                Metadata: new JsonObject { ["experimentId"] = experiment.Id, ["itemId"] = item.ItemId }))
            .ToList();
        await SendAsync(scores, $"item {item.ItemId}", cancellationToken);
    }

    public async Task OnRunCompletedAsync(EvalRunResult result, CancellationToken cancellationToken)
    {
        if (result.Plan.Experiment is not { } experiment || result.Metrics.Count == 0)
        {
            return;
        }

        var scores = result.Metrics
            .Select(metric => new LangfuseScore(
                ScoreId(experiment.Id, "run", metric.Key),
                metric.Key,
                metric.Value.Mean,
                DatasetRunId: experiment.Id,
                Comment: string.Create(CultureInfo.InvariantCulture, $"Mean over {metric.Value.Count} item(s)."),
                Environment: experiment.Environment))
            .ToList();
        await SendAsync(scores, "the run", cancellationToken);
    }

    // Deterministic, so sending a score again updates it in Langfuse instead of adding a duplicate.
    internal static string ScoreId(string experimentId, string subject, string metric)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{experimentId}\n{subject}\n{metric}"));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }

    internal static string? Comment(EvalItemResultDto item, string metric)
    {
        string? comment = metric switch
        {
            EvalMetrics.AnswerFactRecall when item.MissingFacts.Count > 0 => "Missing: " + string.Join("; ", item.MissingFacts),
            EvalMetrics.CitationValidity when item.Citations.Any(citation => !citation.Valid) =>
                "Unknown tags: " + string.Join(", ", item.Citations.Where(citation => !citation.Valid).Select(citation => citation.Ref)),
            EvalMetrics.JudgeFaithfulness => Criterion(item, "faithfulness"),
            EvalMetrics.JudgeRelevance => Criterion(item, "relevance"),
            EvalMetrics.JudgeCorrectness => Criterion(item, "correctness"),
            _ => null,
        };

        return comment is null || comment.Length <= MaxCommentLength ? comment : comment[..MaxCommentLength].TrimEnd() + "…";
    }

    private static string? Criterion(EvalItemResultDto item, string name) =>
        item.Judgement?.Criteria.FirstOrDefault(criterion => criterion.Name == name) is { } criterion
            ? criterion.Reasoning
            : null;

    private async Task SendAsync(IReadOnlyList<LangfuseScore> scores, string subject, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.CreateScoresAsync(scores, cancellationToken);
            if (result.Rejected > 0)
            {
                logger.LogWarning("Langfuse rejected {Count} score(s) for {Subject}: {Errors}", result.Rejected, subject, string.Join("; ", result.Errors));
                _warnings.Enqueue($"Langfuse rejected {result.Rejected} score(s) for {subject}: {string.Join("; ", result.Errors.Take(3))}");
            }
        }
        catch (LangfuseEvalException ex)
        {
            logger.LogWarning(ex, "Could not send scores for {Subject} to Langfuse", subject);
            _warnings.Enqueue($"Scores for {subject} were not sent to Langfuse: {ex.Message}");
        }
    }
}
