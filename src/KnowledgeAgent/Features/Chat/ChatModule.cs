using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace KnowledgeAgent.Features.Chat;

public static class ChatModule
{
    public static IHostApplicationBuilder AddChat(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddOptions<AgentOptions>().Bind(builder.Configuration.GetSection(AgentOptions.SectionName));

        builder.AddAgents();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<AgentCatalog>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, SearchKnowledgeBaseTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, GrepSourcesTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, FindSourcesTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, ReadSourceTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, RecordFeedbackTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentTool, RequestHumanHandoverTool>());
        services.TryAddSingleton<ToolRegistry>();
        services.TryAddSingleton<AgentRunner>();
        services.TryAddSingleton<TitleGenerator>();

        // Tools and the title generator run concurrently with the turn, so they open their own scopes.
        services.TryAddScoped<IConversationRepository, ConversationRepository>();
        services.TryAddScoped<IChatService, ChatService>();
        services.TryAddScoped<ChatTurnExecutor>();
        services.TryAddScoped<ReplySuggester>();
        services.TryAddScoped<ConversationTimeline>();
        services.TryAddSingleton<ChatTurnHost>();
        services.TryAddSingleton<ConversationEventHub>();
        services.TryAddSingleton<ConversationNotifications>();

        if (services.Any(descriptor => descriptor.ServiceType == typeof(NpgsqlDataSource)))
        {
            services.TryAddSingleton<IConversationNotifier, PostgresConversationNotifier>();
            services.AddHostedService<PostgresConversationListener>();
        }
        else
        {
            services.TryAddSingleton<IConversationNotifier, NullConversationNotifier>();
        }

        return builder;
    }
}
