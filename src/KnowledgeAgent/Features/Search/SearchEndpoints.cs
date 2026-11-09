using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Search;

internal static class SearchEndpoints
{
    public const int DefaultTopK = 6;
    public const int MaxTopK = 50;
    public const int MaxQueryLength = 4000;

    // Comma-separated reasons when part of the search failed and was skipped, for example "keyword_failed".
    public const string DegradedHeader = "X-Search-Degraded";

    public static RouteHandlerBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/search", SearchAsync)
            .WithTags("Search")
            .WithName("Search")
            .WithSummary("Hybrid search: vector + keyword retrieval fused by rank, then reranked when a reranker is configured.")
            .WithDescription(
                "mode: hybrid (default from Search:Mode), vector or keyword. rerank: true/false overrides Search:Rerank. "
                + "minScore filters vector candidates by cosine similarity. Each hit reports its vector, keyword and rerank scores. "
                + "When a part of the search failed and was skipped, the " + DegradedHeader + " response header names it.");

    private static async Task<Results<Ok<SearchHitDto[]>, ValidationProblem>> SearchAsync(
        SearchRequest request,
        IKnowledgeSearch search,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            errors.Add("query", "A query is required.");
        }
        else if (request.Query.Length > MaxQueryLength)
        {
            errors.Add("query", $"The query must not exceed {MaxQueryLength} characters.");
        }

        if (request.TopK is < 1 or > MaxTopK)
        {
            errors.Add("topK", $"topK must be between 1 and {MaxTopK}.");
        }

        if (request.MinScore is { } minScore && (double.IsNaN(minScore) || minScore < -1 || minScore > 1))
        {
            errors.Add("minScore", "minScore must be between -1 and 1.");
        }

        var mode = request.Mode is null ? null : SearchModes.Normalize(request.Mode);
        if (request.Mode is not null && mode is null)
        {
            errors.Add("mode", $"mode must be one of: {string.Join(", ", SearchModes.All)}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var sourceIds = request.SourceIds is { Count: > 0 } ids ? ids.Distinct().ToArray() : null;
        var result = await search.SearchAsync(
            new KnowledgeSearchRequest(request.Query.Trim(), request.TopK ?? DefaultTopK, sourceIds, request.MinScore, mode, request.Rerank),
            cancellationToken);
        if (result.IsDegraded)
        {
            response.Headers[DegradedHeader] = string.Join(',', result.Degraded);
        }

        return TypedResults.Ok(result.Hits.Select(h => h.ToDto()).ToArray());
    }
}
