using System.Security.Claims;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Feedback;

internal static class FeedbackEndpoints
{
    public const int DefaultTake = 100;

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/feedback").WithTags("Feedback");
        group.MapGet("/", ListAsync)
            .WithName("ListFeedback")
            .WithSummary("Feedback, newest first; filter by widget, agent, kind (answer, conversation, agent), positive and days.");
        group.MapGet("/summary", SummaryAsync)
            .WithName("SummarizeFeedback")
            .WithSummary("Thumbs-up ratio, average rating and counts per widget and agent over the last days.");

        routes.MapPost("/conversations/{id:guid}/feedback", SubmitAsync)
            .WithTags("Feedback")
            .WithName("RateConversation")
            .WithSummary("A staff rating of an answer (1 or -1, with messageId) or of the whole conversation (1 to 5).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        return routes;
    }

    private static async Task<Results<Ok<IReadOnlyList<FeedbackDto>>, ValidationProblem>> ListAsync(
        Guid? widgetId,
        Guid? agentId,
        string? kind,
        bool? positive,
        int? days,
        int? take,
        IFeedbackService feedback,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        FeedbackKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<FeedbackKind>(kind, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return ApiResults.Validation("kind", "kind must be answer, conversation or agent.");
            }

            kindFilter = parsed;
        }

        if (days is < 1 or > 3650 || take is < 1 or > 500)
        {
            return ApiResults.Validation(days is < 1 or > 3650 ? "days" : "take", "days must be 1 to 3650 and take 1 to 500.");
        }

        var from = days is { } window ? timeProvider.GetUtcNow().AddDays(-window) : (DateTimeOffset?)null;
        var filter = new FeedbackFilter(widgetId, agentId, kindFilter, positive, from);
        return TypedResults.Ok(await feedback.ListAsync(filter, take ?? DefaultTake, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<FeedbackSummaryDto>>, ValidationProblem>> SummaryAsync(
        int? days,
        IFeedbackService feedback,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (days is < 1 or > 3650)
        {
            return ApiResults.Validation("days", "days must be 1 to 3650.");
        }

        return TypedResults.Ok(await feedback.SummarizeAsync(timeProvider.GetUtcNow().AddDays(-(days ?? 30)), cancellationToken));
    }

    private static async Task<Results<Ok<FeedbackDto>, ProblemHttpResult>> SubmitAsync(
        Guid id,
        SubmitFeedbackRequest request,
        ClaimsPrincipal user,
        IConversationRepository conversations,
        IFeedbackService feedback,
        CancellationToken cancellationToken)
    {
        if (await conversations.GetAsync(id, includeTurns: false, cancellationToken) is not { } conversation)
        {
            return ApiResults.NotFound($"Conversation '{id}' was not found.");
        }

        var author = new FeedbackAuthorRef(FeedbackAuthor.Staff, StaffKey(user));
        return TypedResults.Ok(await feedback.SubmitAsync(conversation, request, author, cancellationToken));
    }

    // The email when Keycloak sends one, so a person's ratings stay theirs across sessions.
    internal static string StaffKey(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity?.Name ?? "staff";
}
