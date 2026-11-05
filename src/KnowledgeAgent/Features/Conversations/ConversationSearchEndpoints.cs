using System.Security.Claims;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Conversations;

internal static class ConversationSearchEndpoints
{
    public const int MaxTitleLength = 200;
    private const int MaxQueryLength = 500;

    public static IEndpointRouteBuilder MapConversationSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/conversations").WithTags("Conversations");

        group.MapGet("/", ListAsync)
            .WithName("ListConversations")
            .WithSummary("Conversations, pinned first and then the most recently updated, with keyset paging (pass nextCursor as cursor). "
                + "q searches titles and messages; labelId may repeat and matches conversations with any of the labels; contactId keeps one person's, segmentId the people of a segment, createdBy (a subject or me) one staff member's. "
                + "status takes one or more statuses separated by commas; assignee is me, none or a staff member's id; unread=true keeps what the signed-in user has not read.")
            .ProducesValidationProblem();

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateConversation")
            .WithSummary("Renames or pins a conversation; fields left out stay as they are. Pins are shared by everyone.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/search", SearchAsync)
            .WithName("SearchConversations")
            .WithSummary("Searches the titles and messages of every conversation, with filters, sorting and keyset paging (pass nextCursor as cursor). "
                + "status takes one or more statuses separated by commas; assignee is me, none or a staff member's id; unread=true keeps what the signed-in user has not read.")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}/history", HistoryAsync)
            .WithName("GetConversationHistory")
            .WithSummary("Every rating and every request for a person of a conversation, newest first.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    private static async Task<Results<Ok<ConversationListPageDto>, ValidationProblem>> ListAsync(
        string? q,
        string? channel,
        Guid? widgetId,
        string? status,
        Guid[]? labelId,
        bool? pinned,
        Guid? contactId,
        Guid? segmentId,
        string? createdBy,
        string? assignee,
        bool? unread,
        string? cursor,
        int? limit,
        ClaimsPrincipal user,
        IConversationSearch search,
        ISegmentService segments,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var query = ParseFilters(errors, q, channel, widgetId, status, labelId, pinned, cursor, limit) with { ContactId = contactId, CreatedBy = CreatedBy(createdBy, user, errors) };
        query = WithAssignee(query, assignee, user, errors) with { ViewerId = StaffClaims.Id(user), Unread = unread };
        query = await WithSegmentAsync(query, segmentId, user, segments, timeProvider, errors, cancellationToken);
        if (query.After is { Pinned: null })
        {
            errors["cursor"] = ["cursor is not a cursor this list returned."];
        }

        return errors.Count > 0 ? ApiResults.Validation(errors) : TypedResults.Ok(await search.ListAsync(query, cancellationToken));
    }

    private static async Task<Results<Ok<ConversationSummaryDto>, ProblemHttpResult, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdateConversationRequest request,
        ClaimsPrincipal user,
        IConversationRepository conversations,
        IConversationSearch search,
        CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (request.Title is not null && title is not { Length: > 0 and <= MaxTitleLength })
        {
            return ApiResults.Validation("title", $"title must be 1 to {MaxTitleLength} characters.");
        }

        if (await conversations.GetAsync(id, includeTurns: false, cancellationToken) is null)
        {
            return ApiResults.NotFound($"Conversation '{id}' was not found.");
        }

        if (title is not null)
        {
            await conversations.UpdateTitleAsync(id, title, cancellationToken);
        }

        if (request.Pinned is { } pinned)
        {
            await conversations.SetPinnedAsync(id, pinned, cancellationToken);
        }

        var updated = await search.ListAsync(new ConversationSearchQuery { Id = id, Limit = 1, ViewerId = StaffClaims.Id(user) }, cancellationToken);
        return updated.Items is [var item] ? TypedResults.Ok(item) : ApiResults.NotFound($"Conversation '{id}' was not found.");
    }

    private static async Task<Results<Ok<ConversationSearchPageDto>, ValidationProblem>> SearchAsync(
        string? q,
        string? channel,
        Guid? widgetId,
        string? status,
        bool? handover,
        bool? includeTests,
        int? minRating,
        int? maxRating,
        bool? thumbsDown,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid[]? labelId,
        bool? pinned,
        Guid? contactId,
        Guid? segmentId,
        string? createdBy,
        string? assignee,
        bool? unread,
        string? sort,
        string? order,
        string? cursor,
        int? limit,
        ClaimsPrincipal user,
        IConversationSearch search,
        ISegmentService segments,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var filters = ParseFilters(errors, q, channel, widgetId, status, labelId, pinned, cursor, limit) with { ContactId = contactId, CreatedBy = CreatedBy(createdBy, user, errors) };
        filters = WithAssignee(filters, assignee, user, errors) with { ViewerId = StaffClaims.Id(user), Unread = unread };
        filters = await WithSegmentAsync(filters, segmentId, user, segments, timeProvider, errors, cancellationToken);

        if (minRating is < 1 or > 5 || maxRating is < 1 or > 5 || minRating > maxRating)
        {
            errors["minRating"] = ["minRating and maxRating must be 1 to 5, the minimum not above the maximum."];
        }

        if (from is not null && to is not null && from >= to)
        {
            errors["from"] = ["from must be before to."];
        }

        var sortKey = sort?.Trim().ToLowerInvariant() switch
        {
            null or "" or "updated" => ConversationSortKey.Updated,
            "created" => ConversationSortKey.Created,
            _ => (ConversationSortKey?)null,
        };
        if (sortKey is null)
        {
            errors["sort"] = ["sort must be updated or created."];
        }

        var descending = order?.Trim().ToLowerInvariant() switch
        {
            null or "" or "desc" => true,
            "asc" => false,
            _ => (bool?)null,
        };
        if (descending is null)
        {
            errors["order"] = ["order must be desc or asc."];
        }

        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        var query = filters with
        {
            HasHandover = handover,
            IncludeTests = includeTests ?? false,
            MinRating = minRating,
            MaxRating = maxRating,
            ThumbsDown = thumbsDown ?? false,
            From = from,
            To = to,
            Sort = sortKey!.Value,
            Descending = descending!.Value,
        };
        return TypedResults.Ok(await search.SearchAsync(query, cancellationToken));
    }

    private static async Task<ConversationSearchQuery> WithSegmentAsync(
        ConversationSearchQuery query,
        Guid? segmentId,
        ClaimsPrincipal user,
        ISegmentService segments,
        TimeProvider timeProvider,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        if (segmentId is not { } id)
        {
            return query;
        }

        var viewer = new SegmentViewer(StaffClaims.Id(user), StaffClaims.DisplayName(user), StaffClaims.Role(user) == AuthRoles.Admin);
        if (await segments.FindAsync(id, viewer, cancellationToken) is not { } segment)
        {
            errors["segmentId"] = ["segmentId is not a segment you can see."];
            return query;
        }

        return query with { ContactConditions = await segments.ConditionsAsync(segment, cancellationToken), Now = timeProvider.GetUtcNow() };
    }

    private static ConversationSearchQuery ParseFilters(
        Dictionary<string, string[]> errors,
        string? q,
        string? channel,
        Guid? widgetId,
        string? status,
        Guid[]? labelId,
        bool? pinned,
        string? cursor,
        int? limit)
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
                errors["channel"] = ["channel must be portal, widget, api or mcp."];
            }
        }

        var statusTexts = (status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var statuses = statusTexts.Select(ConversationStatuses.Parse).ToList();
        if (statuses.Any(parsed => parsed is null))
        {
            errors["status"] = ["status must be ai, handover_pending, human or closed, or several of them separated by commas."];
        }

        var statusList = statuses.OfType<ConversationStatus>().Distinct().ToList();
        ConversationStatus? statusFilter = statusList is [var single] ? single : null;

        var labelIds = (labelId ?? []).Distinct().ToList();
        if (labelIds.Count > ConversationSearchQuery.MaxLabelIds)
        {
            errors["labelId"] = [$"Filter by at most {ConversationSearchQuery.MaxLabelIds} labels."];
        }

        if (!ConversationCursor.TryDecode(cursor, out var after))
        {
            errors["cursor"] = ["cursor is not a cursor this API returned."];
        }

        if (limit is < 1 or > ConversationSearchQuery.MaxLimit)
        {
            errors["limit"] = [$"limit must be 1 to {ConversationSearchQuery.MaxLimit}."];
        }

        if (q is { Length: > MaxQueryLength })
        {
            errors["q"] = [$"q must be at most {MaxQueryLength} characters."];
        }

        return new ConversationSearchQuery
        {
            Text = q,
            Channel = channelFilter,
            WidgetId = widgetId,
            Status = statusFilter,
            Statuses = statusList.Count > 1 ? statusList : [],
            LabelIds = labelIds,
            Pinned = pinned,
            After = after,
            Limit = limit ?? ConversationSearchQuery.DefaultLimit,
        };
    }

    private static ConversationSearchQuery WithAssignee(ConversationSearchQuery query, string? assignee, ClaimsPrincipal user, Dictionary<string, string[]> errors)
    {
        var value = assignee?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return query;
        }

        if (value.Length > StaffMember.MaxIdLength)
        {
            errors["assignee"] = [$"assignee must be me, none or a staff member's id of at most {StaffMember.MaxIdLength} characters."];
            return query;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return query with { Unassigned = true };
        }

        if (string.Equals(value, "me", StringComparison.OrdinalIgnoreCase))
        {
            if (StaffClaims.Id(user) is { } me)
            {
                return query with { AssigneeId = me };
            }

            errors["assignee"] = ["The signed-in user has no id, so assignee=me matches nothing."];
            return query;
        }

        return query with { AssigneeId = value };
    }

    private static string? CreatedBy(string? createdBy, ClaimsPrincipal user, Dictionary<string, string[]> errors)
    {
        var value = createdBy?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > 200)
        {
            errors["createdBy"] = ["createdBy must be at most 200 characters."];
            return null;
        }

        return string.Equals(value, "me", StringComparison.OrdinalIgnoreCase) ? user.FindFirst("sub")?.Value ?? value : value;
    }

    private static async Task<Results<Ok<ConversationHistoryDto>, ProblemHttpResult>> HistoryAsync(
        Guid id,
        IConversationSearch search,
        CancellationToken cancellationToken) =>
        await search.GetHistoryAsync(id, cancellationToken) is { } history
            ? TypedResults.Ok(history)
            : ApiResults.NotFound($"Conversation '{id}' was not found.");
}
