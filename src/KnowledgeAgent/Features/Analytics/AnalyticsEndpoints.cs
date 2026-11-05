using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Analytics;

internal static class AnalyticsEndpoints
{
    public const int DefaultDays = 30;
    public const int MaxDays = 90;

    public static RouteHandlerBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/analytics", GetAsync)
            .WithTags("Analytics")
            .WithName("GetAnalytics")
            .WithSummary("Conversations per day, the share answered without a person, ratings and thumbs over time, each widget's and each segment's numbers and the questions the agent could not answer (30 days by default).")
            .ProducesValidationProblem();

    private static async Task<Results<Ok<AnalyticsDto>, ValidationProblem>> GetAsync(
        int? days,
        IAnalyticsRepository analytics,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxDays)
        {
            return ApiResults.Validation("days", $"days must be 1 to {MaxDays}.");
        }

        var now = timeProvider.GetUtcNow();
        var result = await analytics.GetAsync(now, days ?? DefaultDays, cancellationToken);
        return TypedResults.Ok(result with { Segments = await analytics.SegmentsAsync(now, days ?? DefaultDays, cancellationToken) });
    }
}
