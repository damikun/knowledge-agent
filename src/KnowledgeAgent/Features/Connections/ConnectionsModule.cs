using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Connections.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Connections;

public static class ConnectionsModule
{
    public static IHostApplicationBuilder AddConnections(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<McpOptions>().BindConfiguration(McpOptions.SectionName);
        services.AddDataProtection();
        services.TryAddSingleton<McpSecretProtector>();
        services.TryAddSingleton<McpClientPool>();
        services.TryAddScoped<IMcpServerRepository, McpServerRepository>();
        services.TryAddScoped<IMcpServerService, McpServerService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentToolSource, McpToolSource>());
        return builder;
    }
}
