using System.Diagnostics;
using System.Diagnostics.Metrics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Search;

internal static class SearchTelemetry
{
    public static readonly ActivitySource ActivitySource = new(KnowledgeAgentTelemetry.Search);

    private static readonly Meter Meter = new(KnowledgeAgentTelemetry.Search);

    public static readonly Counter<long> Degraded = Meter.CreateCounter<long>(
        "knowledge.search.degraded", unit: "{search}", description: "Searches that skipped a failed part (keyword, rerank, source lookup), by reason.");

    public static void RecordDegraded(Activity? activity, IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return;
        }

        activity?.SetTag(Tags.Degraded, reasons.ToArray());
        foreach (var reason in reasons)
        {
            Degraded.Add(1, new KeyValuePair<string, object?>(Tags.DegradedReason, reason));
        }
    }

    public static class Tags
    {
        public const string TopK = "knowledge.search.top_k";
        public const string HitCount = "knowledge.search.hits";
        public const string SearchMode = "knowledge.search.mode";
        public const string VectorHits = "knowledge.search.vector_hits";
        public const string KeywordHits = "knowledge.search.keyword_hits";
        public const string KeywordTerms = "knowledge.search.keyword_terms";
        public const string Reranker = "knowledge.search.reranker";
        public const string EmbeddingModel = "knowledge.embedding.model";
        public const string Degraded = "knowledge.search.degraded";
        public const string DegradedReason = "reason";
    }
}
