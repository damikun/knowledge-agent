using System.Security.Claims;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Handover;

internal static class HandoverEndpoints
{
    public static RouteGroupBuilder MapHandoverEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/conversations/{id:guid}").WithTags("Handover");
        group.MapGet("/handover", async Task<Results<Ok<HandoverDto>, NoContent>> (Guid id, IHandoverService handover, CancellationToken cancellationToken) =>
                await handover.GetLatestAsync(id, cancellationToken) is { } latest ? TypedResults.Ok(latest) : TypedResults.NoContent())
            .WithName("GetHandover")
            .WithSummary("The latest request for a person in the conversation, if any.");
        group.MapPost("/join", async (Guid id, ClaimsPrincipal user, IHandoverService handover, CancellationToken cancellationToken) =>
                TypedResults.Ok((await handover.JoinAsync(id, StaffName(user), StaffClaims.Ref(user), cancellationToken)).ToDto()))
            .WithName("JoinConversation")
            .WithSummary("Takes the conversation over from the agent; the visitor sees who joined.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/messages", async (Guid id, StaffMessageRequest request, ClaimsPrincipal user, IHandoverService handover, CancellationToken cancellationToken) =>
            {
                var message = await handover.AddStaffMessageAsync(id, StaffName(user), request.Text, HandoverService.ViaPortal, StaffClaims.Ref(user), cancellationToken);
                return TypedResults.Ok(ConversationMessageMappings.ToDto(message));
            })
            .WithName("ReplyToVisitor")
            .WithSummary("Sends a reply to the visitor; joins the conversation first if nobody has.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/return", async (Guid id, ClaimsPrincipal user, IHandoverService handover, CancellationToken cancellationToken) =>
            {
                await handover.ReturnToAgentAsync(id, StaffName(user), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("ReturnToAgent")
            .WithSummary("Hands the conversation back to the agent.");
        group.MapPost("/close", async (Guid id, ClaimsPrincipal user, IHandoverService handover, CancellationToken cancellationToken) =>
            {
                await handover.CloseAsync(id, StaffName(user), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("CloseConversation")
            .WithSummary("Closes the conversation; the visitor can start a new one.");
        group.MapPost("/assign", AssignAsync)
            .WithName("AssignConversation")
            .WithSummary("Assigns the conversation to a staff member who signed in before (userId), or unassigns it (userId null). The visitor sees nothing.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<Results<Ok<AssignmentDto>, ValidationProblem>> AssignAsync(
        Guid id,
        AssignRequest request,
        ClaimsPrincipal user,
        IStaffDirectory staff,
        IHandoverService handover,
        CancellationToken cancellationToken)
    {
        StaffRef? assignee = null;
        var userId = request.UserId?.Trim();
        if (!string.IsNullOrEmpty(userId))
        {
            if (userId == StaffClaims.Id(user))
            {
                // Taking it oneself works even before the portal recorded the sign-in.
                await staff.SeenAsync(user, cancellationToken);
                assignee = StaffClaims.Ref(user);
            }
            else if (await staff.GetAsync(userId, cancellationToken) is { } member)
            {
                assignee = new StaffRef(member.Id, member.Name);
            }
            else
            {
                return ApiResults.Validation("userId", "userId must be a staff member who has signed in to the portal.");
            }
        }

        await handover.AssignAsync(id, assignee, cancellationToken);
        return TypedResults.Ok(new AssignmentDto(assignee is null ? null : new ConversationAssigneeDto(assignee.Id, assignee.Name)));
    }

    internal static string StaffName(ClaimsPrincipal user)
    {
        var name = user.FindFirst("given_name")?.Value ?? user.FindFirst("name")?.Value ?? user.Identity?.Name ?? user.FindFirst("preferred_username")?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Support" : name.Trim();
    }
}
