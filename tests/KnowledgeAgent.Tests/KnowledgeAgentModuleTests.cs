using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.Overview;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Tests;

public sealed class KnowledgeAgentModuleTests
{
    [Fact]
    public async Task Registers_every_feature_with_its_own_data_access()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["VectorStore:Provider"] = "InMemory",
            ["Prompts:Store"] = "Local",
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        builder.AddKnowledgeAgent();
        using var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ISourceRepository>().ShouldBeOfType<SourceRepository>();
        services.GetRequiredService<IConversationRepository>().ShouldBeOfType<ConversationRepository>();
        services.GetRequiredService<IEvalRunRepository>().ShouldBeOfType<EvalRunRepository>();
        services.GetRequiredService<IModelProfileOverrideStore>().ShouldBeOfType<ModelProfileOverrideStore>();
        services.GetRequiredService<IProviderSettingsStore>().ShouldBeOfType<ProviderSettingsStore>();
        services.GetRequiredService<IKnowledgeSearch>().ShouldBeOfType<KnowledgeSearch>();
        services.GetRequiredService<IChatService>().ShouldBeOfType<ChatService>();
        services.GetRequiredService<IProviderSettingsService>().ShouldBeOfType<ProviderSettingsService>();
        services.GetRequiredService<IOverviewRepository>().ShouldBeOfType<OverviewRepository>();
    }
}
