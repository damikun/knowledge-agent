using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;
using KnowledgeAgent.Infrastructure.AI.Providers.Ollama;
using KnowledgeAgent.Infrastructure.AI.Providers.OpenAI;
using KnowledgeAgent.Infrastructure.AI.Providers.Voyage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Infrastructure.AI;

public static class AiModule
{
    public static IHostApplicationBuilder AddAi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration;
        var services = builder.Services;

        services.AddLogging();
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .PostConfigure(options => AiOptionsPostConfigure.Apply(options, configuration));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiProvider, AnthropicAiProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiProvider, OpenAiProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiProvider, OllamaAiProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiProvider, VoyageAiProvider>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProviderErrorLog, ProviderErrorLog>();
        services.TryAddSingleton<AiProviderRegistry>();
        services.TryAddSingleton<ModelSettingsNotifier>();
        services.TryAddSingleton<IModelProfileResolver, ModelProfileResolver>();
        services.TryAddSingleton<IChatClientFactory, ChatClientFactory>();
        services.TryAddSingleton<IEmbeddingService, EmbeddingService>();

        services.AddDataProtection();
        services.TryAddSingleton<ProviderApiKeyProtector>();
        services.TryAddSingleton<AiOptionsSource>();
        services.TryAddSingleton<IAiOptionsSource>(serviceProvider => serviceProvider.GetRequiredService<AiOptionsSource>());
        services.TryAddScoped<IModelProfileOverrideStore, ModelProfileOverrideStore>();
        services.TryAddScoped<IProviderSettingsStore, ProviderSettingsStore>();

        return builder;
    }
}
