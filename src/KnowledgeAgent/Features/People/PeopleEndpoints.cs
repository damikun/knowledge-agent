using System.Security.Claims;
using System.Text.Json;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.People;

internal static partial class PeopleEndpoints
{
    private const int MaxQueryLength = 200;

    public static RouteGroupBuilder MapPeopleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/people").WithTags("People");

        group.MapGet("/", ListAsync)
            .WithName("ListPeople")
            .WithSummary("People who talked to a widget, the most recently seen first, with keyset paging (pass nextCursor as cursor). "
                + "q searches names, emails and user ids; identified=true keeps the users the website signed in. "
                + "filter (base64url JSON of [{attr, op, value}]) or repeated f=attr:op:value narrows by attributes, all conditions together; segmentId uses a saved segment. "
                + "Without a filter only people with a conversation or a sign-in show; all=true shows every contact.")
            .ProducesValidationProblem();
        group.MapGet("/count", CountAsync)
            .WithName("CountPeople")
            .WithSummary("How many people match the same filters as the list.")
            .ProducesValidationProblem();
        group.MapGet("/attributes", ListAttributesAsync)
            .WithName("ListContactAttributes")
            .WithSummary("The attribute registry (custom keys with type, label and how many contacts have them), the built-in attributes and the operators of each type.");
        group.MapGet("/attributes/{key}/values", AttributeValuesAsync)
            .WithName("ListContactAttributeValues")
            .WithSummary("The most common values of a text attribute, for filter suggestions.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/attributes/{key}", UpdateAttributeAsync)
            .WithName("UpdateContactAttribute")
            .WithSummary("Renames an attribute or changes whether it shows, reaches the agent and which sources may set it (trust: any, verified or server).")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/attributes/{key}", DeleteAttributeAsync)
            .WithName("DeleteContactAttribute")
            .WithSummary("Deletes an attribute and removes it from every contact. Send { \"confirm\": true }.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/{id:guid}/attributes", UpdateAttributesAsync)
            .WithName("UpdatePersonAttributes")
            .WithSummary("Sets custom attributes of a person (source admin); null clears one. Keys not in the request stay.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetPerson")
            .WithSummary("A person with their attributes, the widgets they used and their numbers.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdatePerson")
            .WithSummary("Changes the name, email or notes; a field left out stays, an empty one is cleared.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id:guid}", EraseAsync)
            .WithName("ErasePerson")
            .WithSummary("Erases the person with every conversation, rating and request for a person (right to erasure). Send { \"confirm\": true }.")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/export", ExportAsync)
            .WithName("ExportPerson")
            .WithSummary("Everything stored about the person as one JSON file (right of access).")
            .RequireAuthorization(AuthPolicies.Admin)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<Results<Ok<PeoplePageDto>, ValidationProblem>> ListAsync(
        string? q,
        Guid? widgetId,
        bool? identified,
        string? filter,
        string[]? f,
        Guid? segmentId,
        bool? all,
        string? cursor,
        int? limit,
        ClaimsPrincipal user,
        IPeopleRepository people,
        ISegmentService segments,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!ConversationCursor.TryDecode(cursor, out var after) || after is { Pinned: not null })
        {
            errors["cursor"] = ["cursor is not a cursor this list returned."];
        }

        if (limit is < 1 or > PeopleQuery.MaxLimit)
        {
            errors["limit"] = [$"limit must be 1 to {PeopleQuery.MaxLimit}."];
        }

        var query = await QueryAsync(q, widgetId, identified, filter, f, segmentId, all, user, people, segments, timeProvider, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return ApiResults.Validation(errors);
        }

        return TypedResults.Ok(await people.ListAsync(query with { After = after, Limit = limit ?? PeopleQuery.DefaultLimit }, cancellationToken));
    }

    private static async Task<Results<Ok<PeopleCountDto>, ValidationProblem>> CountAsync(
        string? q,
        Guid? widgetId,
        bool? identified,
        string? filter,
        string[]? f,
        Guid? segmentId,
        bool? all,
        ClaimsPrincipal user,
        IPeopleRepository people,
        ISegmentService segments,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var query = await QueryAsync(q, widgetId, identified, filter, f, segmentId, all, user, people, segments, timeProvider, errors, cancellationToken);
        return errors.Count > 0 ? ApiResults.Validation(errors) : TypedResults.Ok(new PeopleCountDto(await people.CountAsync(query, cancellationToken)));
    }

    private static async Task<PeopleQuery> QueryAsync(
        string? q,
        Guid? widgetId,
        bool? identified,
        string? filter,
        string[]? f,
        Guid? segmentId,
        bool? all,
        ClaimsPrincipal user,
        IPeopleRepository people,
        ISegmentService segments,
        TimeProvider timeProvider,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        if (q is { Length: > MaxQueryLength })
        {
            errors["q"] = [$"q must be at most {MaxQueryLength} characters."];
        }

        var conditions = new List<ResolvedCondition>();
        if (!ContactFilters.TryParse(filter, f, out var parsed, out var error))
        {
            errors["filter"] = [error!];
        }
        else if (parsed.Count > 0)
        {
            var problems = new List<string>();
            conditions.AddRange(ContactFilters.Resolve(parsed, await people.AttributeRegistryAsync(cancellationToken), strict: true, problems));
            if (problems.Count > 0)
            {
                errors["filter"] = [.. problems];
            }
        }

        if (segmentId is { } id)
        {
            if (await segments.FindAsync(id, Viewer(user), cancellationToken) is { } segment)
            {
                conditions.AddRange(await segments.ConditionsAsync(segment, cancellationToken));
            }
            else
            {
                errors["segmentId"] = ["segmentId is not a segment you can see."];
            }
        }

        var filtered = conditions.Count > 0 || segmentId is not null;
        return new PeopleQuery
        {
            Text = q,
            WidgetId = widgetId,
            Identified = identified,
            Conditions = conditions,
            Now = timeProvider.GetUtcNow(),
            ActiveOnly = all != true && !filtered,
        };
    }

    internal static SegmentViewer Viewer(ClaimsPrincipal user) =>
        new(StaffClaims.Id(user), StaffClaims.DisplayName(user), StaffClaims.Role(user) == AuthRoles.Admin);

    private static async Task<Ok<ContactAttributeListDto>> ListAttributesAsync(IPeopleRepository people, CancellationToken cancellationToken) =>
        TypedResults.Ok(new ContactAttributeListDto(
            await people.ListAttributesAsync(cancellationToken),
            ContactFilterAttributes.All,
            AttributeTypes.All.ToDictionary(type => type, type => ContactFilterOps.For(type, builtIn: false), StringComparer.Ordinal)));

    private static async Task<Results<Ok<IReadOnlyList<AttributeValueCountDto>>, ProblemHttpResult>> AttributeValuesAsync(
        string key,
        IPeopleRepository people,
        CancellationToken cancellationToken)
    {
        var registry = await people.AttributeRegistryAsync(cancellationToken);
        return registry.ContainsKey(key)
            ? TypedResults.Ok(await people.AttributeValuesAsync(key, 20, cancellationToken))
            : ApiResults.NotFound($"Attribute '{key}' was not found.");
    }

    private static async Task<Results<Ok<ContactAttributeDto>, ProblemHttpResult, ValidationProblem>> UpdateAttributeAsync(
        string key,
        UpdateContactAttributeRequest request,
        IPeopleRepository people,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (request.Label is not null && request.Label.Trim() is not { Length: > 0 and <= ContactAttributeDefinition.MaxLabelLength })
        {
            errors.Add("label", $"The label must be 1 to {ContactAttributeDefinition.MaxLabelLength} characters.");
        }

        if (request.Trust is not null && !AttributeTrust.All.Contains(request.Trust))
        {
            errors.Add("trust", "trust must be any, verified or server.");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        if (await people.UpdateAttributeAsync(key, request, cancellationToken) is null)
        {
            return ApiResults.NotFound($"Attribute '{key}' was not found.");
        }

        return TypedResults.Ok((await people.ListAttributesAsync(cancellationToken)).First(item => item.Key == key));
    }

    private static async Task<Results<Ok<DeleteContactAttributeResultDto>, ProblemHttpResult, ValidationProblem>> DeleteAttributeAsync(
        string key,
        [FromBody] DeleteContactAttributeRequest? request,
        IPeopleRepository people,
        CancellationToken cancellationToken)
    {
        if (request is not { Confirm: true })
        {
            return ApiResults.Validation("confirm", "Deleting an attribute removes its value from every contact. Send confirm: true to go ahead.");
        }

        return await people.DeleteAttributeAsync(key, cancellationToken) is { } contacts
            ? TypedResults.Ok(new DeleteContactAttributeResultDto(contacts))
            : ApiResults.NotFound($"Attribute '{key}' was not found.");
    }

    private static async Task<Results<Ok<AttributeChangeDto>, ProblemHttpResult, ValidationProblem>> UpdateAttributesAsync(
        Guid id,
        UpdatePersonAttributesRequest request,
        IPeopleRepository people,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.Attributes is not { Count: > 0 } attributes)
        {
            return ApiResults.Validation("attributes", "Send at least one attribute.");
        }

        if (await people.SetAttributesAsync(id, attributes, AttributeSources.Admin, timeProvider.GetUtcNow(), cancellationToken) is not { } result)
        {
            return NotFound(id);
        }

        if (result.Rejected.Count > 0)
        {
            return TypedResults.ValidationProblem(result.Rejected.GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Select(item => item.Reason).ToArray()));
        }

        return TypedResults.Ok(new AttributeChangeDto(result.Applied, result.Rejected));
    }

    private static async Task<Results<Ok<PersonDto>, ProblemHttpResult>> GetAsync(Guid id, IPeopleRepository people, CancellationToken cancellationToken) =>
        await people.GetProfileAsync(id, cancellationToken) is { } person ? TypedResults.Ok(person) : NotFound(id);

    private static async Task<Results<Ok<PersonDto>, ProblemHttpResult, ValidationProblem>> UpdateAsync(
        Guid id,
        UpdatePersonRequest request,
        IPeopleRepository people,
        CancellationToken cancellationToken)
    {
        if (await people.GetAsync(id, cancellationToken) is not { } contact)
        {
            return NotFound(id);
        }

        var errors = new ValidationErrors();
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length > Contact.MaxNameLength)
            {
                errors.Add("name", $"The name can be at most {Contact.MaxNameLength} characters long.");
            }

            contact.Name = name.Length == 0 ? null : name;
        }

        if (request.Email is not null)
        {
            var email = request.Email.Trim();
            if (email.Length > 0 && !WidgetEmails.IsValid(email))
            {
                errors.Add("email", "Enter a valid email address.");
            }

            contact.Email = email.Length == 0 ? null : email;
        }

        if (request.Notes is not null)
        {
            var notes = request.Notes.Trim();
            if (notes.Length > Contact.MaxNotesLength)
            {
                errors.Add("notes", $"Notes can be at most {Contact.MaxNotesLength} characters long.");
            }

            contact.Notes = notes.Length == 0 ? null : notes;
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        await people.UpdateAsync(contact, cancellationToken);
        return TypedResults.Ok((await people.GetProfileAsync(id, cancellationToken))!);
    }

    private static async Task<Results<Ok<ErasePersonResultDto>, ProblemHttpResult, ValidationProblem>> EraseAsync(
        Guid id,
        [FromBody] ErasePersonRequest? request,
        IPeopleRepository people,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Erasure cannot be undone, so it needs an explicit yes in the body, not only the verb.
        if (request is not { Confirm: true })
        {
            return ApiResults.Validation("confirm", "Erasing a person deletes all their conversations. Send confirm: true to go ahead.");
        }

        if (await people.EraseAsync(id, cancellationToken) is not { } result)
        {
            return NotFound(id);
        }

        LogErased(loggerFactory.CreateLogger(typeof(PeopleEndpoints).FullName!), id, result.Conversations, result.Visitors);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<JsonHttpResult<PersonExportDto>, ProblemHttpResult>> ExportAsync(
        Guid id,
        HttpResponse response,
        IPeopleRepository people,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await people.ExportAsync(id, timeProvider.GetUtcNow(), cancellationToken) is not { } export)
        {
            return NotFound(id);
        }

        response.Headers.ContentDisposition = $"attachment; filename=\"person-{id}.json\"";
        response.Headers.CacheControl = "no-store";
        return TypedResults.Json(export);
    }

    private static ProblemHttpResult NotFound(Guid id) => ApiResults.NotFound($"Person '{id}' was not found.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Erased person {ContactId} with {Conversations} conversations and {Visitors} browsers")]
    private static partial void LogErased(ILogger logger, Guid contactId, int conversations, int visitors);
}
