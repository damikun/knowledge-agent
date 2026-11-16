using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class AgentScopeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_agent_over_every_source_searches_Internal_sources()
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

        host.Search.Requests[2].SourceIds.ShouldNotBeNull().ShouldBe([guide.Id, faq.Id], ignoreOrder: true, "a chat without an agent runs as the oldest one");
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
        var docsOnly = host.Agents.Add(new AgentConfig { Key = "docs", Name = "Docs", FolderIds = [docs.Id] });
        await using var scope = host.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAgentResolver>();

        (await resolver.ResolveScopeAsync(website, [], Ct)).ShouldBe([guide.Id]);
        (await resolver.ResolveScopeAsync(website, [pricing.Id, guide.Id], Ct)).ShouldBe([guide.Id]);
        (await resolver.ResolveScopeAsync(docsOnly, [], Ct)).ShouldBe([guide.Id]);
        var everySource = new AgentConfig { Key = "all", Name = "All" };
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

    [Fact]
    public async Task A_chat_without_an_agent_runs_as_the_oldest_agent()
    {
        using var host = new AgentTestHost();
        host.Agents.Add(new AgentConfig { Key = "newer", Name = "Newer", ProfileName = "newer", CreatedAt = AgentTestHost.Now });
        host.Agents.Add(new AgentConfig { Key = "older", Name = "Older", ProfileName = "older", CreatedAt = AgentTestHost.Now.AddYears(-2) });
        var older = host.ChatClients.Script("older").ThenText("Hello.");

        var events = await host.ChatAsync("Hi", cancellationToken: Ct);

        events.Done().FinishReason.ShouldBe(ChatFinishReasons.Stop);
        older.Requests.ShouldHaveSingleItem();
    }
}
