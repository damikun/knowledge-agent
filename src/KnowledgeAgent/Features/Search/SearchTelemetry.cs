using System.Diagnostics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Search;

internal static class SearchTelemetry
{
    public static readonly ActivitySource ActivitySource = new(KnowledgeAgentTelemetry.Search);

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
    }
}
