using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Overview;

internal static class OverviewEndpoints
{
    public const int DefaultDays = 7;
    public const int MaxDays = 90;

    public static RouteHandlerBuilder MapOverviewEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/overview", GetAsync)
            .WithTags("Overview")
            .WithName("GetOverview")
            .WithSummary("Conversations started in the last days (7 by default), how many went to a person, the average rating and each widget's activity.")
            .ProducesValidationProblem();

    private static async Task<Results<Ok<OverviewDto>, ValidationProblem>> GetAsync(
        int? days,
        IOverviewRepository overview,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxDays)
        {
            return ApiResults.Validation("days", $"days must be 1 to {MaxDays}.");
        }

        return TypedResults.Ok(await overview.GetAsync(timeProvider.GetUtcNow(), days ?? DefaultDays, cancellationToken));
    }
}
