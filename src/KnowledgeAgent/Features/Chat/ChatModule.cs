using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeAgent.Features.Chat;

public static class ChatModule
{
    public static IHostApplicationBuilder AddChat(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddOptions<AgentOptions>().Bind(builder.Configuration.GetSection(AgentOptions.SectionName));
        services.AddOptions<HistoryOptions>().Bind(builder.Configuration.GetSection(HistoryOptions.SectionName));

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
        services.TryAddSingleton<ConversationHistoryBuilder>();

        // Tools and the title generator run concurrently with the turn, so they open their own scopes.
        services.TryAddScoped<IConversationRepository, ConversationRepository>();
        services.TryAddScoped<IChatService, ChatService>();
        services.TryAddScoped<ChatTurnExecutor>();
        services.TryAddScoped<ReplySuggester>();
        services.TryAddScoped<ConversationTimeline>();
        services.TryAddSingleton<ChatTurnHost>();
        services.TryAddSingleton<ConversationEventHub>();

        builder.AddLive();

        return builder;
    }

    private static void AddLive(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection(LiveOptions.SectionName);
        var provider = section.Get<LiveOptions>()?.Provider;

        // AddPersistence runs first and registers the data source only for PostgreSQL.
        var hasDataSource = services.Any(descriptor => descriptor.ServiceType == typeof(NpgsqlDataSource));
        services.AddOptions<LiveOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LiveOptions>>(new LiveOptionsValidator(hasDataSource)));
        services.TryAddSingleton<ConversationLiveRelay>();
        if (LiveProviders.UsesPostgres(provider, hasDataSource))
        {
            services.TryAddSingleton<LivePayload>();
            services.TryAddSingleton<ILiveBus, PostgresLiveBus>();
            services.AddHostedService<PostgresLiveListener>();
        }
        else
        {
            services.TryAddSingleton<ILiveBus, InProcessLiveBus>();
        }
    }
}
