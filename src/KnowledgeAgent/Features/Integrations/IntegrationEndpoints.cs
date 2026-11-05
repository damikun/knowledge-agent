using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace KnowledgeAgent.Features.Integrations;

internal static class IntegrationEndpoints
{
    public static RouteGroupBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/integrations").WithTags("Integrations");
        group.MapGet("/", async (IntegrationService service, CancellationToken cancellationToken) => TypedResults.Ok(await service.GetAsync(cancellationToken)))
            .WithName("GetIntegrations")
            .WithSummary("Email and Slack settings for handovers; passwords and tokens are never returned.");
        group.MapPut("/email", async (SaveEmailSettingsRequest request, IntegrationService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.SaveEmailAsync(request, cancellationToken)))
            .WithName("SaveEmailIntegration")
            .ProducesValidationProblem();
        group.MapPut("/slack", async (SaveSlackSettingsRequest request, IntegrationService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.SaveSlackAsync(request, cancellationToken)))
            .WithName("SaveSlackIntegration")
            .ProducesValidationProblem();
        group.MapPost("/email/test", async ([FromBody] TestEmailRequest? request, IntegrationService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.TestEmailAsync(request, cancellationToken)))
            .WithName("TestEmailIntegration")
            .WithSummary("Sends a test email to the given address or the support address, and signs in to IMAP when replies are set up.")
            .ProducesValidationProblem();
        group.MapPost("/slack/test", async (IntegrationService service, CancellationToken cancellationToken) => TypedResults.Ok(await service.TestSlackAsync(cancellationToken)))
            .WithName("TestSlackIntegration")
            .WithSummary("Checks both tokens and the bot sign-in, and posts a test message to the channel when one is set.");
        return group;
    }
}
