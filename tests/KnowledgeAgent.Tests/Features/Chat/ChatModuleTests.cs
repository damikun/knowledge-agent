using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
}
