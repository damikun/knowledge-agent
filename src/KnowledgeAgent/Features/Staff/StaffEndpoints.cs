using KnowledgeAgent.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Staff;

internal static class StaffEndpoints
{
    public static RouteHandlerBuilder MapStaffEndpoints(this IEndpointRouteBuilder routes)
    {
        // Settings > Team: admins only, unlike the staff list every staff member needs to assign conversations.
        routes.MapGet("/team", async Task<Ok<TeamDto>> (TeamService team, CancellationToken cancellationToken) =>
                TypedResults.Ok(await team.GetAsync(cancellationToken)))
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Staff")
            .WithName("GetTeam")
            .WithSummary("Everyone who signed in, with their role, when they were last seen and their conversations of the last 30 days, and the Keycloak console link.");

        return routes.MapGet("/staff", async Task<Ok<IReadOnlyList<StaffMemberDto>>> (IStaffDirectory staff, CancellationToken cancellationToken) =>
                TypedResults.Ok(await staff.ListAsync(cancellationToken)))
            .WithTags("Staff")
            .WithName("ListStaff")
            .WithSummary("Everyone who signed in to the portal, by name: the people conversations can be assigned to.");
    }
}
