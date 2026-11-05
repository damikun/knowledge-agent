using System.Security.Claims;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.People;

internal static class SegmentEndpoints
{
    public static RouteGroupBuilder MapSegmentEndpoints(this IEndpointRouteBuilder routes)
    {
        // Staff read, create and edit segments (their own and shared ones); only admins delete them.
        var group = routes.MapGroup("/segments").WithTags("Segments");

        group.MapGet("/", ListAsync)
            .WithName("ListSegments")
            .WithSummary("Saved contact filters the caller can see (shared ones and their own), with how many contacts match now (counted at most once a minute).");
        group.MapPost("/", CreateAsync)
            .WithName("CreateSegment")
            .WithSummary("Saves a filter as a segment: name, description, filter ([{attr, op, value}]), color, icon and whether it is shared.")
            .ProducesValidationProblem();
        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateSegment")
            .WithSummary("Changes a segment; fields left out stay as they are.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSegment")
            .WithSummary("Deletes a segment; the contacts stay.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<Ok<IReadOnlyList<SegmentDto>>> ListAsync(ClaimsPrincipal user, ISegmentService segments, CancellationToken cancellationToken) =>
        TypedResults.Ok(await segments.ListAsync(PeopleEndpoints.Viewer(user), cancellationToken));

    private static async Task<Created<SegmentDto>> CreateAsync(SaveSegmentRequest request, ClaimsPrincipal user, ISegmentService segments, CancellationToken cancellationToken)
    {
        var segment = await segments.CreateAsync(request, PeopleEndpoints.Viewer(user), cancellationToken);
        return TypedResults.Created($"/api/segments/{segment.Id}", segment);
    }

    private static async Task<Results<Ok<SegmentDto>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        SaveSegmentRequest request,
        ClaimsPrincipal user,
        ISegmentService segments,
        CancellationToken cancellationToken) =>
        await segments.UpdateAsync(id, request, PeopleEndpoints.Viewer(user), cancellationToken) is { } segment
            ? TypedResults.Ok(segment)
            : ApiResults.NotFound($"Segment '{id}' was not found.");

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, ISegmentService segments, CancellationToken cancellationToken) =>
        await segments.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : ApiResults.NotFound($"Segment '{id}' was not found.");
}
