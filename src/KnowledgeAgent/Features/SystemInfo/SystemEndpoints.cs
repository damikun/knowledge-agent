using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.SystemInfo;

internal static class SystemEndpoints
{
    private static readonly string Version = AppVersion.Of(typeof(SystemEndpoints).Assembly);

    public static RouteHandlerBuilder MapSystemEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/system/info", GetInfo)
            .WithTags("System")
            .WithName("GetSystemInfo")
            .WithSummary("Providers, stores, the Langfuse link and the public address of this instance.");

    // Every staff member reads the public address; only admins change it.
    public static IEndpointRouteBuilder MapPublicAddressEndpoints(this IEndpointRouteBuilder routes)
    {
        const string path = "/system/public-address";
        routes.MapGet(path, async (IPublicAddress address, CancellationToken cancellationToken) => TypedResults.Ok(await address.GetAsync(cancellationToken)))
            .WithTags("System")
            .WithName("GetPublicAddress")
            .WithSummary("The public address of this installation and where it comes from: saved, configuration or email.");
        routes.MapPut(path, async (SavePublicAddressRequest request, IPublicAddress address, CancellationToken cancellationToken) =>
                TypedResults.Ok(await address.SaveAsync(request.Url, cancellationToken)))
            .WithTags("System")
            .WithName("SavePublicAddress")
            .WithSummary("Saves the public address, such as https://support.example.com; an empty url removes it so App:PublicUrl applies.")
            .ProducesValidationProblem()
            .RequireAuthorization(AuthPolicies.Admin);
        return routes;
    }

    public static RouteHandlerBuilder MapSystemHealthEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/system/health", GetHealth)
            .WithTags("System")
            .WithName("GetSystemHealth")
            .WithSummary("The state of each component: database, search index, jobs, sign-in, Langfuse, models, email and Slack.");

    private static async Task<Ok<SystemHealthDto>> GetHealth(SystemHealthService health, CancellationToken cancellationToken) =>
        TypedResults.Ok(await health.GetAsync(cancellationToken));

    private static async Task<Ok<SystemInfoDto>> GetInfo(
        IConfiguration configuration,
        IHostEnvironment environment,
        IVectorStore vectorStore,
        IProviderSettingsService providerSettings,
        IPublicAddress publicAddress,
        CancellationToken cancellationToken)
    {
        var persistence = Bind<PersistenceOptions>(configuration, PersistenceOptions.SectionName);
        var prompts = Bind<PromptOptions>(configuration, PromptOptions.SectionName);
        var langfuse = Bind<LangfuseOptions>(configuration, LangfuseOptions.SectionName);
        var agents = Bind<AgentOptions>(configuration, AgentOptions.SectionName);

        var langfuseUrl = langfuse.IsConfigured
            ? (langfuse.PublicUrl.TrimToNull() ?? langfuse.BaseUrl.TrimToNull())?.TrimEnd('/')
            : null;

        return TypedResults.Ok(new SystemInfoDto(
            Version,
            environment.EnvironmentName,
            persistence.Provider,
            vectorStore.ProviderName,
            prompts.Store,
            langfuse.IsConfigured,
            langfuseUrl,
            agents.ResearcherEnabled,
            await providerSettings.ListAsync(cancellationToken),
            await publicAddress.GetAsync(cancellationToken)));
    }

    // Read from configuration directly, since each module owns its own options registration.
    private static T Bind<T>(IConfiguration configuration, string section) where T : class, new() =>
        configuration.GetSection(section).Get<T>() ?? new T();
}
