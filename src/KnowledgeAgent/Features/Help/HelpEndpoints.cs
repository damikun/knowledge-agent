using System.Security.Claims;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Help;

internal static class HelpEndpoints
{
    public const string TurnedOffMessage = "The in-app help assistant is turned off. An admin can turn it on under Settings > System.";

    public static RouteGroupBuilder MapHelpEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/help").WithTags("Help");

        group.MapGet("/", StatusAsync)
            .WithName("GetHelpStatus")
            .WithSummary("Whether the in-app help assistant is on, whether a chat model is connected (without one the help is disabled), and the setup steps done so far.");

        group.MapPut("/settings", SaveAsync)
            .WithName("SaveHelpSettings")
            .WithSummary("Turns the in-app help assistant on or off for everyone.")
            .RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/chat", ChatAsync)
            .WithName("HelpChat")
            .WithSummary("Answers a question about the product from its help and streams the answer as text/event-stream, with the event types of POST /api/chat. Nothing is stored.")
            .Produces<ChatStreamEvent>(StatusCodes.Status200OK, ChatEventStreamResult.ContentType)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Ok<HelpStatusDto>> StatusAsync(
        ClaimsPrincipal user,
        IHelpSettingsStore settings,
        HelpSetupReader setup,
        CancellationToken cancellationToken)
    {
        var admin = user.IsInRole(AuthRoles.Admin);
        if (!(await settings.GetAsync(cancellationToken)).Enabled)
        {
            return TypedResults.Ok(new HelpStatusDto(false, admin, false, null));
        }

        var facts = await setup.ReadAsync(cancellationToken);
        return TypedResults.Ok(new HelpStatusDto(true, admin, facts.ChatModel, facts));
    }

    private static async Task<Ok<HelpStatusDto>> SaveAsync(
        SaveHelpSettingsRequest request,
        ClaimsPrincipal user,
        IHelpSettingsStore settings,
        HelpSetupReader setup,
        CancellationToken cancellationToken)
    {
        await settings.SaveAsync(new HelpSettings { Enabled = request.Enabled }, cancellationToken);
        return await StatusAsync(user, settings, setup, cancellationToken);
    }

    private static async Task<Results<ChatEventStreamResult, ValidationProblem, ProblemHttpResult>> ChatAsync(
        HelpChatRequest request,
        ClaimsPrincipal user,
        IHelpSettingsStore settings,
        HelpChatService help,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!(await settings.GetAsync(cancellationToken)).Enabled)
        {
            return ApiResults.NotFound(TurnedOffMessage);
        }

        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length is 0 or > HelpChatService.MaxMessageLength)
        {
            return ApiResults.Validation("message", $"Enter a question of 1 to {HelpChatService.MaxMessageLength} characters.");
        }

        if (request.History is { Count: > 50 })
        {
            return ApiResults.Validation("history", "Send at most 50 earlier messages.");
        }

        request = request with { Message = message, Page = ValidPage(request.Page) };
        var events = help.StreamAsync(request, user.IsInRole(AuthRoles.Admin), cancellationToken).GetAsyncEnumerator(cancellationToken);
        bool hasFirst;
        try
        {
            hasFirst = await events.MoveNextAsync();
        }
        catch
        {
            await events.DisposeAsync();
            throw;
        }

        return new ChatEventStreamResult(events, hasFirst, loggerFactory.CreateLogger(typeof(ChatEventStreamResult).FullName!));
    }

    private static string? ValidPage(string? page) =>
        page is { Length: > 1 and <= 300 } && page.StartsWith('/') && !page.StartsWith("//", StringComparison.Ordinal) ? page : null;
}
