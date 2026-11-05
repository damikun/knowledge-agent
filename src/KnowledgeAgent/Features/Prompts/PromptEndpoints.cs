using System.Text.Json;
using KnowledgeAgent.Features.Prompts.Rendering;
using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Prompts;

internal static partial class PromptEndpoints
{
    public const int MaxTemplateLength = 200_000;

    public static RouteGroupBuilder MapPromptEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/prompts").WithTags("Prompts");

        group.MapGet("/", ListAsync)
            .WithName("ListPrompts")
            .WithSummary("Prompts the application uses, with origin (langfuse | local), version and variables.");

        group.MapPost("/render", RenderTemplate)
            .WithName("RenderTemplate")
            .WithSummary("Render an ad-hoc Handlebars template (playground).");

        group.MapGet("/{name}", GetAsync)
            .WithName("GetPrompt")
            .WithSummary("Raw template of a prompt (optionally a specific label or version).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{name}/render", RenderAsync)
            .WithName("RenderPrompt")
            .WithSummary("Render a prompt with variables (preview).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPromptEditEndpoints();

        return group;
    }

    private static async Task<Ok<PromptSummaryDto[]>> ListAsync(IPromptService prompts, CancellationToken cancellationToken)
    {
        var summaries = await prompts.ListAsync(cancellationToken);
        return TypedResults.Ok(summaries.Select(s => s.ToDto()).ToArray());
    }

    private static async Task<Results<Ok<PromptDetailDto>, ValidationProblem>> GetAsync(
        string name,
        string? label,
        int? version,
        IPromptService prompts,
        IPromptTemplateRenderer renderer,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (version is < 1)
        {
            return ApiResults.Validation("version", "version must be greater than 0.");
        }

        var template = await prompts.GetTemplateAsync(name, Selector(label, version), cancellationToken);
        var logger = loggerFactory.CreateLogger(typeof(PromptEndpoints).FullName!);
        var variables = ExtractVariables(renderer, template, logger);
        var externalUrl = template.Origin == PromptOrigin.Langfuse
            ? await FindExternalUrlAsync(prompts, template.Name, logger, cancellationToken)
            : null;
        return TypedResults.Ok(template.ToDetailDto(variables, externalUrl));
    }

    private static async Task<Results<Ok<RenderedPromptDto>, ValidationProblem>> RenderAsync(
        string name,
        [FromBody] RenderPromptRequest? request,
        IPromptService prompts,
        CancellationToken cancellationToken)
    {
        if (request?.Version is < 1)
        {
            return ApiResults.Validation("version", "version must be greater than 0.");
        }

        var rendered = await prompts.RenderAsync(
            name,
            ToVariables(request?.Variables),
            Selector(request?.Label, request?.Version),
            cancellationToken);
        return TypedResults.Ok(rendered.ToDto());
    }

    private static Results<Ok<RenderTemplateResponse>, ValidationProblem> RenderTemplate(
        RenderTemplateRequest request,
        IPromptService prompts,
        IPromptTemplateRenderer renderer)
    {
        if (request.Template is null)
        {
            return ApiResults.Validation("template", "A template is required.");
        }

        if (request.Template.Length > MaxTemplateLength)
        {
            return ApiResults.Validation("template", $"The template must not exceed {MaxTemplateLength} characters.");
        }

        var output = prompts.RenderTemplate(request.Template, ToVariables(request.Variables));
        return TypedResults.Ok(new RenderTemplateResponse(output, renderer.ExtractVariables(request.Template)));
    }

    internal static IReadOnlyDictionary<string, object?> ToVariables(IReadOnlyDictionary<string, JsonElement>? variables) =>
        variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : variables.ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.Ordinal);

    private static PromptSelector? Selector(string? label, int? version) =>
        string.IsNullOrWhiteSpace(label) && version is null ? null : new PromptSelector(label.TrimToNull(), version);

    private static IReadOnlyList<string> ExtractVariables(IPromptTemplateRenderer renderer, PromptTemplate template, ILogger logger)
    {
        var parts = template.Type == PromptType.Chat
            ? template.Messages.Select(m => m.Content)
            : [template.Text ?? string.Empty];

        var variables = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            try
            {
                variables.UnionWith(renderer.ExtractVariables(part));
            }
            catch (PromptRenderException exception)
            {
                // A broken stored template is still shown so it can be fixed, just without a variable list.
                LogVariablesUnavailable(logger, template.Name, exception.Message);
            }
        }

        return variables.ToArray();
    }

    private static async Task<string?> FindExternalUrlAsync(IPromptService prompts, string name, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var summaries = await prompts.ListAsync(cancellationToken);
            return summaries.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))?.ExternalUrl;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException)
        {
            LogExternalUrlUnavailable(logger, name, exception.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Variables of prompt {Prompt} could not be extracted: {Reason}")]
    private static partial void LogVariablesUnavailable(ILogger logger, string prompt, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "External URL of prompt {Prompt} is unavailable: {Reason}")]
    private static partial void LogExternalUrlUnavailable(ILogger logger, string prompt, string reason);
}
