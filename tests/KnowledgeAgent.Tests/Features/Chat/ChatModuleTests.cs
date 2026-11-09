using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class ChatModuleTests
{
    [Fact]
    public void Registers_the_chat_service_and_binds_agent_options()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?>
        {
            ["Agents:AppName"] = "Acme Assistant",
            ["Agents:MaxIterations"] = "3",
            ["Agents:ResearcherMaxIterations"] = "2",
            ["Agents:OrchestratorProfile"] = "main",
            ["Agents:ResearcherProfile"] = "deep",
            ["Agents:DefaultTopK"] = "4",
            ["Agents:MaxTopK"] = "9",
        });

        var options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
        options.AppName.ShouldBe("Acme Assistant");
        options.MaxIterations.ShouldBe(3);
        options.MaxTopK.ShouldBe(9);

        using (var scope = host.Services.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            chat.ShouldBeOfType<ChatService>();
            scope.ServiceProvider.GetRequiredService<IChatService>().ShouldBeSameAs(chat, "scoped");
        }

        host.Services.GetRequiredService<AgentRunner>().ShouldBeSameAs(host.Services.GetRequiredService<AgentRunner>());
        host.Services.GetRequiredService<TimeProvider>().ShouldBeSameAs(host.Time);

        var catalog = host.Services.GetRequiredService<AgentCatalog>();
        catalog.Orchestrator.Name.ShouldBe("orchestrator");
        catalog.Orchestrator.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        catalog.Orchestrator.ProfileName.ShouldBe("main");
        catalog.Orchestrator.MaxIterations.ShouldBe(3);
        catalog.Orchestrator.Delegation.ShouldBeNull();
        catalog.Orchestrator.ToolNames.ShouldBe(
            [
                ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource, ToolNames.DelegateResearch,
                ToolNames.RecordFeedback, ToolNames.RequestHumanHandover,
            ]);
        catalog.Researcher.PromptName.ShouldBe(PromptNames.ResearcherSystem);
        catalog.Researcher.ProfileName.ShouldBe("deep");
        catalog.Researcher.MaxIterations.ShouldBe(2);
        catalog.Researcher.ToolNames.ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.ReadSource]);
        catalog.Researcher.Delegation!.ToolName.ShouldBe(ToolNames.DelegateResearch);
        catalog.SubAgents.ShouldBe([catalog.Researcher]);

        var search = host.Services.GetServices<IAgentTool>().OfType<SearchKnowledgeBaseTool>().Single();
        var topK = search.Declaration.JsonSchema.GetProperty("properties").GetProperty("top_k");
        topK.GetProperty("maximum").GetInt32().ShouldBe(9);
        topK.GetProperty("description").GetString().ShouldBe("Number of passages to return (default 4, max 9).");
    }

    [Fact]
    public void Disabling_the_researcher_removes_the_delegate_tool()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:ResearcherEnabled"] = "false" });

        var catalog = host.Services.GetRequiredService<AgentCatalog>();
        catalog.SubAgents.ShouldBeEmpty();
        catalog.Orchestrator.ToolNames.ShouldNotContain(ToolNames.DelegateResearch);
        host.Services.GetRequiredService<ToolRegistry>().TryGet(ToolNames.DelegateResearch, out _).ShouldBeFalse();
    }

    [Fact]
    public void Keeps_a_time_provider_registered_before_the_module_and_adds_one_otherwise()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddChat().ShouldBeSameAs(builder);

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
        provider.GetRequiredService<IOptions<AgentOptions>>().Value.MaxIterations.ShouldBe(8);
        builder.Services.Count(d => d.ServiceType == typeof(IChatService)).ShouldBe(1);
        builder.Services.Single(d => d.ServiceType == typeof(IChatService)).Lifetime.ShouldBe(ServiceLifetime.Scoped);
        builder.Services.Count(d => d.ServiceType == typeof(IAgentTool)).ShouldBe(6);

        builder.AddChat();
        builder.Services.Count(d => d.ServiceType == typeof(IAgentTool)).ShouldBe(6, "registration is idempotent");
    }

    [Fact]
    public void Uses_the_in_process_bus_without_a_postgres_database_and_registers_no_listener()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddChat();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<ILiveBus>().ShouldBeOfType<InProcessLiveBus>();
        provider.GetServices<IHostedService>().OfType<PostgresLiveListener>().ShouldBeEmpty();
    }

    [Fact]
    public void Uses_the_postgres_bus_and_one_listener_when_the_database_is_postgres()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        // Never opened: building the bus and the listener does not connect.
        using var dataSource = NpgsqlDataSource.Create("Host=localhost");
        builder.Services.AddSingleton(dataSource);
        builder.AddChat();
        builder.AddChat();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<ILiveBus>().ShouldBeOfType<PostgresLiveBus>();
        provider.GetServices<IHostedService>().OfType<PostgresLiveListener>().ShouldHaveSingleItem();
        provider.GetServices<IValidateOptions<LiveOptions>>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Live_provider_in_process_wins_over_a_postgres_database()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Live:Provider"] = "InProcess" });
        using var dataSource = NpgsqlDataSource.Create("Host=localhost");
        builder.Services.AddSingleton(dataSource);
        builder.AddChat();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<ILiveBus>().ShouldBeOfType<InProcessLiveBus>();
        provider.GetServices<IHostedService>().OfType<PostgresLiveListener>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Redis", "Live:Provider 'Redis' is not supported")]
    [InlineData("Postgres", "Live:Provider is Postgres but Persistence:Provider is not")]
    public async Task A_live_provider_that_cannot_work_stops_the_host_from_starting(string provider, string error)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceModule.InMemoryProvider,
            ["Persistence:InMemoryDatabaseName"] = $"live-{Guid.NewGuid():N}",
            ["Live:Provider"] = provider,
        });
        builder.AddPersistence();
        builder.AddChat();
        using var host = builder.Build();

        var failure = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(error);
        host.Services.GetRequiredService<ILiveBus>().ShouldBeOfType<InProcessLiveBus>();
    }
}
