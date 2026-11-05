using System.Security.Claims;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Conversations;

internal static class ConversationReadEndpoints
{
    public static IEndpointRouteBuilder MapConversationReadEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/conversations/{id:guid}/read").WithTags("Conversations");
        group.MapPost("/", MarkReadAsync)
            .WithName("MarkConversationRead")
            .WithSummary("Marks the conversation read up to sequence (every message when left out) for the signed-in user; the position only moves forward.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/", MarkUnreadAsync)
            .WithName("MarkConversationUnread")
            .WithSummary("Marks the conversation unread for the signed-in user, from its latest visitor or agent message.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return routes;
    }

    private static async Task<Results<Ok<ConversationReadDto>, ProblemHttpResult, ValidationProblem>> MarkReadAsync(
        Guid id,
        MarkReadRequest? request,
        ClaimsPrincipal user,
        IConversationReads reads,
        CancellationToken cancellationToken)
    {
        if (request?.Sequence is < 0)
        {
            return ApiResults.Validation("sequence", "sequence must be 0 or more.");
        }

        if (StaffClaims.Id(user) is not { } userId)
        {
            return ApiResults.Validation("user", "The signed-in user has no id to keep the read state under.");
        }

        return await reads.MarkReadAsync(userId, id, request?.Sequence, cancellationToken) is { } read
            ? TypedResults.Ok(read)
            : ApiResults.NotFound($"Conversation '{id}' was not found.");
    }

    private static async Task<Results<Ok<ConversationReadDto>, ProblemHttpResult, ValidationProblem>> MarkUnreadAsync(
        Guid id,
        ClaimsPrincipal user,
        IConversationReads reads,
        CancellationToken cancellationToken)
    {
        if (StaffClaims.Id(user) is not { } userId)
        {
            return ApiResults.Validation("user", "The signed-in user has no id to keep the read state under.");
        }

        return await reads.MarkUnreadAsync(userId, id, cancellationToken) is { } read
            ? TypedResults.Ok(read)
            : ApiResults.NotFound($"Conversation '{id}' was not found.");
    }
}
