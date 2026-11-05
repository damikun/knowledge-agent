using System.Security.Claims;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Alerts;

internal static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlertEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/alerts").WithTags("Alerts");
        group.MapGet("/", ListAsync)
            .WithName("ListAlerts")
            .WithSummary("What needs attention now, most severe first. Support users see waiting visitors and missed requests only.");
        group.MapPost("/{key}/snooze", SnoozeAsync)
            .WithName("SnoozeAlert")
            .WithSummary("Hides an alert for some hours, or until its count grows or it gets more severe.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{key}/dismiss", DismissAsync)
            .WithName("DismissAlert")
            .WithSummary("Hides an alert until its count grows, it gets more severe, or it resolves and comes back.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/settings", async (AlertService service, CancellationToken cancellationToken) => TypedResults.Ok(await service.GetSettingsAsync(cancellationToken)))
            .WithName("GetAlertSettings")
            .WithSummary("Whether new alerts go to Slack and email, and which kinds.")
            .RequireAuthorization(AuthPolicies.Admin);
        group.MapPut("/settings", async (SaveAlertSettingsRequest request, AlertService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.SaveSettingsAsync(request, cancellationToken)))
            .WithName("SaveAlertSettings")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem();
        return group;
    }

    private static async Task<Ok<AlertListDto>> ListAsync(ClaimsPrincipal user, AlertService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(user.IsInRole(AuthRoles.Admin) ? AlertAudience.Admin : AlertAudience.Support, cancellationToken));

    private static Task<Results<NoContent, ProblemHttpResult>> SnoozeAsync(
        string key,
        SnoozeAlertRequest request,
        ClaimsPrincipal user,
        AlertService service,
        CancellationToken cancellationToken) =>
        HideAsync(key, request.Hours, user, service, cancellationToken);

    private static Task<Results<NoContent, ProblemHttpResult>> DismissAsync(string key, ClaimsPrincipal user, AlertService service, CancellationToken cancellationToken) =>
        HideAsync(key, hours: null, user, service, cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> HideAsync(
        string key,
        int? hours,
        ClaimsPrincipal user,
        AlertService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.SnoozeAsync(key, hours, UserName(user), cancellationToken);
            return TypedResults.NoContent();
        }
        catch (AlertNotActiveException)
        {
            return ApiResults.NotFound("This alert is not active any more.");
        }
    }

    private static string? UserName(ClaimsPrincipal user) =>
        user.FindFirst("name")?.Value ?? user.Identity?.Name ?? user.FindFirst("preferred_username")?.Value;
}
