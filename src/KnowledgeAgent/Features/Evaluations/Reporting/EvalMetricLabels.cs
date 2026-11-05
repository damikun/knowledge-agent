using KnowledgeAgent.Features.Evaluations.Gate;

namespace KnowledgeAgent.Features.Evaluations.Reporting;

public static class EvalMetricLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [EvalMetrics.RetrievalRecallAt5] = "Retrieval recall@5",
        [EvalMetrics.RetrievalRecallAt10] = "Retrieval recall@10",
        [EvalMetrics.RetrievalMrrAt10] = "Retrieval MRR@10",
        [EvalMetrics.AgentSourceRecall] = "Sources found by agent",
        [EvalMetrics.CitationValidity] = "Valid citations",
        [EvalMetrics.CitationSourceRecall] = "Expected sources cited",
        [EvalMetrics.CitationPrecision] = "Citation precision",
        [EvalMetrics.CitationCoverage] = "Cited paragraphs",
        [EvalMetrics.AnswerFactRecall] = "Expected facts",
        [EvalMetrics.AnswerAbstained] = "Declined unanswerable",
        [EvalMetrics.JudgeFaithfulness] = "Faithfulness (judge)",
        [EvalMetrics.JudgeRelevance] = "Relevance (judge)",
        [EvalMetrics.JudgeCorrectness] = "Correctness (judge)",
        [QualityGate.FailedItemsMetric] = "Failed items",
    };

    public static string Of(string metric) => Labels.TryGetValue(metric, out var label) ? label : metric;
}
