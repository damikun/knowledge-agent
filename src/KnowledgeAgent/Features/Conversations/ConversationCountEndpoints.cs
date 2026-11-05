using System.Security.Claims;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Conversations;

internal static class ConversationCountEndpoints
{
    public static IEndpointRouteBuilder MapConversationCountEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/conversations/counts", CountAsync)
            .WithTags("Conversations")
            .WithName("CountConversations")
            .WithSummary("How many conversations each Inbox view holds: by status, pinned, all, by label, mine and unassigned, and how many of each are unread to the signed-in user. Playground tests count only with includeTests or channel=portal.")
            .ProducesValidationProblem();
        return routes;
    }

    private static async Task<Results<Ok<ConversationCountsDto>, ValidationProblem>> CountAsync(
        string? channel,
        Guid? widgetId,
        Guid? contactId,
        bool? includeTests,
        ClaimsPrincipal user,
        IConversationCounts counts,
        CancellationToken cancellationToken)
    {
        ConversationChannel? channelFilter = null;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (Enum.TryParse<ConversationChannel>(channel, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                channelFilter = parsed;
            }
            else
            {
                return ApiResults.Validation("channel", "channel must be portal, widget, api or mcp.");
            }
        }

        var query = new ConversationCountQuery { Channel = channelFilter, WidgetId = widgetId, ContactId = contactId, IncludeTests = includeTests ?? false, ViewerId = StaffClaims.Id(user) };
        return TypedResults.Ok(await counts.CountAsync(query, cancellationToken));
    }
}
