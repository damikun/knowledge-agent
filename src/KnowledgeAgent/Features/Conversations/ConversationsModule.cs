using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Conversations;

public static class ConversationsModule
{
    public static IHostApplicationBuilder AddConversations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<IConversationSearch, ConversationSearchRepository>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<IConversationCounts, ConversationCountRepository>();
        builder.Services.TryAddScoped<IConversationReads, ConversationReadRepository>();
        return builder;
    }
}
