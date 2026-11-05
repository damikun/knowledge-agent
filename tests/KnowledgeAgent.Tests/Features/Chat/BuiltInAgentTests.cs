using System.Text.Json;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

// The built-in agent gets no connected apps, specialists or person, whatever its row says, and never searches Internal.
public sealed class BuiltInAgentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_built_in_agent_never_gets_connected_apps_specialists_or_a_person_at_answer_time()
    {
        using var host = new AgentTestHost(configureServices: services => services.AddScoped<IAgentToolSource, AppToolSource>());
        var billing = host.Agents.Add(new AgentConfig { Key = "billing", Name = "Billing", ProfileName = "orchestrator", Tools = [ToolNames.SearchKnowledgeBase] });

        // A row saved before the rule: the repository fake keeps it as it is, so the turn itself has to ignore it.
        host.Agents.Add(new AgentConfig
        {
            Key = AgentConfig.InternalKey,
            Name = "Knowledge Agent",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase],
            IsBuiltIn = true,
            McpTools = [new AgentMcpApp { ServerId = Guid.NewGuid(), Mode = McpAppMode.All }],
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices" }],
            HandoverPolicy = HandoverPolicy.WhenUnsure,
            McpServerEnabled = true,
            HttpApiEnabled = true,
        });
        var builtIn = host.ChatClients.Script("orchestrator").ThenText("Hello.");

        await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Hi"),
            new ChatCaller(ConversationChannel.Portal, "Staff", StaffId: "staff-1", HandoverAvailable: true),
            Ct)));

        builtIn.Requests.ShouldHaveSingleItem().Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.SearchKnowledgeBase]);

        // The same configuration on another agent does get them, so the test shows the rule and not a missing setup.
        host.Agents.Add(new AgentConfig
        {
            Key = "front",
            Name = "Front",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase],
            McpTools = [new AgentMcpApp { ServerId = Guid.NewGuid(), Mode = McpAppMode.All }],
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices" }],
            HandoverPolicy = HandoverPolicy.WhenUnsure,
        });
        var front = host.ChatClients.Script("orchestrator").ThenText("Hello.");

        await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Hi", Agent: "front"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid(), HandoverAvailable: true),
            Ct)));

        front.Requests.Last().Options!.Tools!.Select(tool => tool.Name)
            .ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.RequestHumanHandover, AppToolSource.ToolName, ToolNames.TransferToAgent], ignoreOrder: true);
    }

    [Fact]
    public async Task No_agent_over_every_source_searches_Internal_sources_not_even_the_built_in_one()
    {
        using var host = new AgentTestHost();
        var internalFolder = host.Folders.AddInternal();
        var hr = host.Folders.Add("HR", internalFolder.Id);
        var docs = host.Folders.Add("Docs");
        var salaries = host.Sources.Add("Salary bands");
        salaries.FolderId = hr.Id;
        var runbook = host.Sources.Add("On-call runbook");
        runbook.FolderId = internalFolder.Id;
        var guide = host.Sources.Add("User guide");
        guide.FolderId = docs.Id;
        var faq = host.Sources.Add("FAQ");
        host.Agents.Add(new AgentConfig { Key = "website", Name = "Website", ProfileName = "orchestrator", Tools = [ToolNames.SearchKnowledgeBase, ToolNames.ReadSource] });
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "salary" })
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "salary", source_ids = new[] { salaries.Id, faq.Id } })
            .ThenToolCall(ToolNames.ReadSource, new { source_id = salaries.Id })
            .ThenText("I cannot say.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "What are the salary bands?", Agent: "website"), Ct);

        events.Done().FinishReason.ShouldBe("stop");
        host.Search.Requests[0].SourceIds.ShouldNotBeNull().ShouldBe([guide.Id, faq.Id], ignoreOrder: true, "every source but the Internal ones");
        host.Search.Requests[1].SourceIds.ShouldNotBeNull().ShouldBe([faq.Id], "the model cannot ask for an Internal source");
        events.OfType<ToolResultEvent>().Last().IsError.ShouldBeTrue("reading an Internal source is out of scope");
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem).Last().Variables["sourceCount"]
            .ShouldBe(2, "the prompt counts only what the agent may search");

        host.ChatClients.Script("orchestrator").ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "salary" }).ThenText("I cannot say.");

        await host.ChatAsync(new ChatRequestDto(null, "What are the salary bands?"), Ct);

        host.Search.Requests[2].SourceIds.ShouldNotBeNull().ShouldBe([guide.Id, faq.Id], ignoreOrder: true, "an old chat without an agent runs as the built-in one");
    }

    [Fact]
    public async Task Folder_scopes_drop_Internal_folders_even_when_one_was_chosen_before_it_moved_there()
    {
        using var host = new AgentTestHost();
        var internalFolder = host.Folders.AddInternal();
        var docs = host.Folders.Add("Docs");

        var moved = host.Folders.Add("Pricing", internalFolder.Id);
        var guide = host.Sources.Add("User guide");
        guide.FolderId = docs.Id;
        var pricing = host.Sources.Add("Price list");
        pricing.FolderId = moved.Id;
        var runbook = host.Sources.Add("On-call runbook");
        runbook.FolderId = internalFolder.Id;
        var website = host.Agents.Add(new AgentConfig { Key = "website", Name = "Website", FolderIds = [docs.Id, moved.Id] });
        var builtIn = host.Agents.Add(new AgentConfig { Key = AgentConfig.InternalKey, Name = "Knowledge Agent", FolderIds = [docs.Id], IsBuiltIn = true });
        await using var scope = host.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAgentResolver>();

        (await resolver.ResolveScopeAsync(website, [], Ct)).ShouldBe([guide.Id]);
        (await resolver.ResolveScopeAsync(website, [pricing.Id, guide.Id], Ct)).ShouldBe([guide.Id]);
        (await resolver.ResolveScopeAsync(builtIn, [], Ct)).ShouldBe([guide.Id]);
        var everySource = new AgentConfig { Key = AgentConfig.InternalKey, Name = "Knowledge Agent", IsBuiltIn = true };
        (await resolver.ResolveScopeAsync(everySource, [runbook.Id, guide.Id], Ct)).ShouldBe([guide.Id], "a chat limited to an Internal source cannot reach it");
    }

    [Fact]
    public async Task Without_Internal_sources_an_agent_over_every_source_searches_unfiltered()
    {
        using var host = new AgentTestHost();
        host.Folders.AddInternal();
        host.Sources.Add("FAQ");
        var website = host.Agents.Add(new AgentConfig { Key = "website", Name = "Website" });

        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IAgentResolver>().ResolveScopeAsync(website, [], Ct)).ShouldBeNull();
    }

    private static async Task<List<ChatStreamEvent>> Drain(IAsyncEnumerable<ChatStreamEvent> stream)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in stream)
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private sealed class AppToolSource : IAgentToolSource
    {
        public const string ToolName = "shop_lookup";

        public Task<IReadOnlyList<IAgentTool>> ResolveAsync(AgentConfig agent, ChatCaller caller, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IAgentTool>>(agent.McpTools.Count == 0 ? [] : [new AppTool()]);
    }

    private sealed class AppTool : IAgentTool
    {
        public string Name => AppToolSource.ToolName;

        public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
            AppToolSource.ToolName,
            "Looks up an order.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone());

        public Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
            Task.FromResult(new ToolResult("Nothing.", "Nothing."));
    }
}
