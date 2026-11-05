using KnowledgeAgent.Features.Widgets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Agents.Access;

public static class AgentAccessModule
{
    public static IHostApplicationBuilder AddAgentAccess(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AgentKeyUsage>();
        services.TryAddSingleton<WidgetRateLimiter>();
        services.TryAddScoped<IAgentApiKeyService, AgentApiKeyService>();
        services.TryAddScoped<AgentKeyAuthenticator>();
        services.TryAddScoped<AgentKeyLookup>();
        services.TryAddScoped<AgentAskService>();
        services.AddAgentMcpServer();
        return builder;
    }

    public static IServiceCollection AddAgentKeyAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyAuth.Scheme, configureOptions: null);
        services.AddAuthorizationBuilder()
            .AddPolicy(AgentKeyAuth.McpPolicy, policy => policy
                .AddAuthenticationSchemes(AgentKeyAuth.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AgentKeyAuth.ChannelClaim, AgentKeyAuth.McpChannel))
            .AddPolicy(AgentKeyAuth.ApiPolicy, policy => policy
                .AddAuthenticationSchemes(AgentKeyAuth.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AgentKeyAuth.ChannelClaim, AgentKeyAuth.ApiChannel));
        return services;
    }
}
