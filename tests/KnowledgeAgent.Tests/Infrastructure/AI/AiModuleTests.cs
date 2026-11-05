using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Infrastructure.AI.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Tests.Infrastructure.AI;

public sealed class AiModuleTests
{
    [Fact]
    public void Registers_the_built_in_providers_once_even_when_called_twice()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(TestConfig.Base());
        builder.AddAi();
        builder.AddAi();
        using var host = builder.Build();

        host.Services.GetServices<IAiProvider>().Select(p => p.Type).ShouldBe(["anthropic", "openai", "ollama", "voyage"]);
        host.Services.GetRequiredService<IModelProfileResolver>().ShouldNotBeNull();
        host.Services.GetRequiredService<IChatClientFactory>().ShouldNotBeNull();
        host.Services.GetRequiredService<IEmbeddingService>().ShouldNotBeNull();
    }

    [Fact]
    public void Additional_providers_are_found_by_type_ignoring_case_and_the_last_registration_wins()
    {
        var replacement = new NamedProvider("Ollama");
        using var host = AiTestHost.Create(configureServices: services =>
        {
            services.AddSingleton<IAiProvider>(new FakeAiProvider());
            services.AddSingleton<IAiProvider>(replacement);
        });

        var registry = host.Get<AiProviderRegistry>();

        registry.Find("FAKE").ShouldBeOfType<FakeAiProvider>();
        registry.Find("ollama").ShouldBeSameAs(replacement);
        registry.Find("unknown").ShouldBeNull();
        registry.Find(null).ShouldBeNull();
    }

    private sealed class NamedProvider(string type) : IAiProvider
    {
        public string Type => type;

        public bool SupportsChat => true;

        public bool SupportsEmbeddings => false;

        public bool IsConfigured(AiProviderOptions options) => true;

        public IChatClient CreateChatClient(AiProviderOptions options, ResolvedModelProfile profile) => new ScriptedChatClient();

        public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options, ResolvedModelProfile profile) =>
            throw new NotSupportedException();
    }
}
