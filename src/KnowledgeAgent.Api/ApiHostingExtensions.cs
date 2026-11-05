using KnowledgeAgent.Features.Agents.Access;
using KnowledgeAgent.Infrastructure.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace KnowledgeAgent.Api;

public static class ApiHostingExtensions
{
    public const string CorsPolicyName = "portal";

    public static IHostApplicationBuilder AddApiServices(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        builder.AddAuth();
        services.AddAgentKeyAuthentication();
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(origin => origin.Trim().TrimEnd('/'))
                .ToArray();
            options.AddPolicy(CorsPolicyName, policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Location"));
        });

        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info ??= new OpenApiInfo();
            document.Info.Title = "Knowledge Agent API";
            document.Info.Version = "v1";
            document.Info.Description =
                "Knowledge sources (URLs, PDFs, text), hybrid search, the agent chat (SSE), model profiles, prompts and evaluations.";
            return Task.CompletedTask;
        }));

        return builder;
    }

    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(CorsPolicyName);
        app.UseAuth();

        app.MapOpenApi().RequireAuthorization(AuthPolicies.Admin);
        app.MapScalarApiReference(options => options.WithTitle("Knowledge Agent API")).RequireAuthorization(AuthPolicies.Admin);

        return app;
    }
}
