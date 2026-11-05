using System.Security.Claims;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Prompts;

internal static class PromptEditEndpoints
{
    public static RouteGroupBuilder MapPromptEditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{name}/versions", ListVersionsAsync)
            .WithName("ListPromptVersions")
            .WithSummary("The newest versions of a prompt in Langfuse with their content and labels, and which one is live.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/{name}/versions", SaveDraftAsync)
            .WithName("SavePromptDraft")
            .WithSummary("Save an edited template as a new Langfuse version without the live label (a draft). The Handlebars syntax is checked first.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        group.MapPost("/{name}/publish", PublishAsync)
            .WithName("PublishPrompt")
            .WithSummary("Move the live label to a version: publishes a draft or rolls back. The application uses it at once.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return group;
    }

    private static async Task<Results<Ok<PromptVersionsDto>, ProblemHttpResult>> ListVersionsAsync(
        string name,
        PromptEditor editor,
        CancellationToken cancellationToken)
    {
        try
        {
            return await editor.ListVersionsAsync(name, cancellationToken) is { } versions
                ? TypedResults.Ok(versions)
                : ApiResults.NotFound($"Langfuse has no prompt named '{name}'.");
        }
        catch (LangfuseApiException exception)
        {
            return LangfuseFailed(exception);
        }
    }

    private static async Task<Results<Created<PromptVersionDto>, ValidationProblem, ProblemHttpResult>> SaveDraftAsync(
        string name,
        SavePromptDraftRequest? request,
        PromptEditor editor,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!editor.Enabled)
        {
            return NotEditable();
        }

        if (request is null)
        {
            return ApiResults.Validation("text", "A template is required.");
        }

        if (request.BaseVersion is < 1)
        {
            return ApiResults.Validation("baseVersion", "baseVersion must be greater than 0.");
        }

        try
        {
            var source = await editor.GetSourceAsync(name, request.BaseVersion, cancellationToken);
            if (source is null)
            {
                return ApiResults.NotFound(request.BaseVersion is { } version
                    ? $"Prompt '{name}' has no version {version} in Langfuse."
                    : $"Langfuse has no prompt named '{name}'.");
            }

            var errors = editor.Validate(source, request);
            if (errors.Count > 0)
            {
                return ApiResults.Validation(errors);
            }

            var created = await editor.SaveDraftAsync(source, request, StaffClaims.DisplayName(user), cancellationToken);
            return TypedResults.Created($"/api/prompts/{Uri.EscapeDataString(name)}/versions", created);
        }
        catch (LangfuseApiException exception)
        {
            return LangfuseFailed(exception);
        }
    }

    private static async Task<Results<Ok<PublishPromptResponse>, ValidationProblem, ProblemHttpResult>> PublishAsync(
        string name,
        PublishPromptRequest? request,
        PromptEditor editor,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!editor.Enabled)
        {
            return NotEditable();
        }

        if (request is null || request.Version < 1)
        {
            return ApiResults.Validation("version", "version must be greater than 0.");
        }

        try
        {
            var published = await editor.PublishAsync(name, request, Author(user), cancellationToken);
            return published is null
                ? ApiResults.NotFound($"Prompt '{name}' has no version {request.Version} in Langfuse.")
                : TypedResults.Ok(published);
        }
        catch (PromptEditConflictException exception)
        {
            return TypedResults.Problem(title: "The live version changed", detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (LangfuseApiException exception)
        {
            return LangfuseFailed(exception);
        }
    }

    private static string Author(ClaimsPrincipal user) =>
        StaffClaims.Id(user) is { } id ? $"{StaffClaims.DisplayName(user)} ({id})" : StaffClaims.DisplayName(user);

    private static ProblemHttpResult NotEditable() => TypedResults.Problem(
        title: "Prompts are not stored in Langfuse",
        detail: "Editing needs Prompts:Store=Langfuse and a configured Langfuse. The built-in defaults are read-only.",
        statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult LangfuseFailed(LangfuseApiException exception) => TypedResults.Problem(
        title: "Langfuse did not accept the request",
        detail: exception.Message,
        statusCode: StatusCodes.Status502BadGateway);
}
