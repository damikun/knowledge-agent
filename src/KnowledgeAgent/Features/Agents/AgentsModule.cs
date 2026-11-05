using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Agents;

public static class AgentsModule
{
    public static IHostApplicationBuilder AddAgents(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAgentRepository, AgentRepository>();
        services.TryAddScoped<IAgentService, AgentService>();
        services.TryAddScoped<IAgentResolver, AgentResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, AgentSeedInitializer>());
        return builder;
    }
}
