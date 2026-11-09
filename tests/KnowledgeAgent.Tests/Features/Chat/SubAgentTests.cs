using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class SubAgentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delegate_research_runs_the_researcher_forwards_its_events_and_shares_citations()
    {
        using var host = new AgentTestHost();
        var pricing = host.Sources.Add("Pricing Guide");
        host.Search.AddHit("Pricing Guide", "The Pro plan costs 10 EUR per month.", sourceId: pricing.Id, page: 2);
        host.Search.AddHit("Support FAQ", "Support is available 24/7.");

        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.DelegateResearch, new { task = "Find pricing and support details", source_ids = new[] { pricing.Id.ToString(), "bogus" } })
            .ThenText("Pro costs 10 EUR [S1]; support is 24/7 [S2].");
        var researcher = host.ChatClients.Script("researcher")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "pricing support" })
            .ThenText("Report: Pro plan 10 EUR/month [S1]. Support 24/7 [S2].", reasoning: "Two relevant passages.");

        var events = await host.ChatAsync("Tell me about pricing and support", cancellationToken: Ct);

        var delegateCall = events.OfType<ToolCallEvent>().First();
        delegateCall.ToolName.ShouldBe(ToolNames.DelegateResearch);
        delegateCall.Agent.ShouldBe("orchestrator");
        delegateCall.ParentCallId.ShouldBeNull();

        var researcherEvents = events.Where(e => e.Agent == "researcher").ToList();
        researcherEvents.ShouldAllBe(e => e.ParentCallId == delegateCall.CallId);
        researcherEvents.CollapsedTypes().ShouldBe(
        [
            ChatEventTypes.AgentStarted,
            ChatEventTypes.StepFinished,
            ChatEventTypes.ToolCall,
            ChatEventTypes.ToolResult,
            ChatEventTypes.ReasoningDelta,
            ChatEventTypes.TextDelta,
            ChatEventTypes.StepFinished,
            ChatEventTypes.AgentCompleted,
        ]);
        researcherEvents.OfType<AgentStartedEvent>().Single().Model.ShouldBe("researcher-model");
        researcherEvents.OfType<AgentCompletedEvent>().Single().Iterations.ShouldBe(2);
        var researcherSearch = researcherEvents.OfType<ToolResultEvent>().Single();
        researcherSearch.Citations.Select(c => c.Ref).ShouldBe(["S1", "S2"]);
        events.Text("researcher").ShouldBe("Report: Pro plan 10 EUR/month [S1]. Support 24/7 [S2].");

        var delegateResult = events.OfType<ToolResultEvent>().Single(e => e.CallId == delegateCall.CallId);
        delegateResult.Agent.ShouldBe("orchestrator");
        delegateResult.IsError.ShouldBeFalse();
        delegateResult.Summary.ShouldBe("Researcher finished after 2 step(s) with 2 citation(s)");
        delegateResult.Citations.Select(c => c.Ref).ShouldBe(["S1", "S2"]);
        orchestrator.Requests[1].Messages[^1].ToolResults().Single().ResultText().Unwrapped()
            .ShouldBe("Report: Pro plan 10 EUR/month [S1]. Support 24/7 [S2].");

        var firstResearcherRequest = researcher.Requests[0];
        firstResearcherRequest.Messages.Count.ShouldBe(2);
        firstResearcherRequest.Messages[0].Role.ShouldBe(ChatRole.System);
        firstResearcherRequest.Messages[0].Text.ShouldBe("You are the researcher of Knowledge Agent. Use at most 6 searches.");
        firstResearcherRequest.Messages[1].Role.ShouldBe(ChatRole.User);
        firstResearcherRequest.Messages[1].Text.ShouldBe("Find pricing and support details");
        firstResearcherRequest.Options!.Tools!.Select(t => t.Name).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.ReadSource]);
        firstResearcherRequest.Options.AdditionalProperties![AgentRunner.PromptNameProperty].ShouldBe(PromptNames.ResearcherSystem);
        firstResearcherRequest.Options.AdditionalProperties[AgentRunner.PromptVersionProperty].ShouldBe(2);

        var render = host.Prompts.RendersOf(PromptNames.ResearcherSystem).ShouldHaveSingleItem();
        render.Variables["appName"].ShouldBe("Knowledge Agent");
        render.Variables["today"].ShouldBe("2026-10-22");
        render.Variables["maxSearches"].ShouldBe(6);
        var focus = render.Variables["focusSources"].ShouldBeAssignableTo<IList<object?>>()!.ShouldHaveSingleItem()
            .ShouldBeAssignableTo<IReadOnlyDictionary<string, object?>>()!;
        focus["id"].ShouldBe(pricing.Id.ToString());
        focus["title"].ShouldBe("Pricing Guide");

        var turn = host.SingleTurn(events.ConversationId());
        var calls = turn.ToolCalls();
        calls.Select(c => (c.ToolName, c.Agent, c.ParentCallId)).ShouldBe(
        [
            (ToolNames.DelegateResearch, "orchestrator", (string?)null),
            (ToolNames.SearchKnowledgeBase, "researcher", delegateCall.CallId),
        ]);
        turn.Citations().Select(c => c.Ref).ShouldBe(["S1", "S2"]);
        orchestrator.Requests.Count.ShouldBe(2);
        var usage = events.OfType<UsageEvent>().Single();
        usage.Agent.ShouldBe("orchestrator");
        usage.InputTokens.ShouldBe(turn.InputTokens);

        turn.ModelMessages().Select(m => m.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);
    }

    [Fact]
    public async Task Usage_includes_the_sub_agent_calls()
    {
        using var host = new AgentTestHost();
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.DelegateResearch, new { task = "Research X" })
            .ThenText("Answer.");
        var researcher = host.ChatClients.Script("researcher").ThenText("Nothing found about X.");

        var events = await host.ChatAsync("X?", cancellationToken: Ct);

        var usage = events.OfType<UsageEvent>().Single();
        (orchestrator.Requests.Count + researcher.Requests.Count).ShouldBe(3);
        usage.InputTokens.ShouldNotBeNull();
        usage.InputTokens.Value.ShouldBe(SumInput(orchestrator) + SumInput(researcher));

        static long SumInput(ScriptedChatClient client) =>
            client.Requests.Sum(r => r.Messages.Sum(m => (long)(m.Text?.Length ?? 0)) / 4 + 1);
    }

    [Fact]
    public async Task Researcher_failures_become_error_results_for_the_orchestrator()
    {
        using var host = new AgentTestHost();
        host.ChatClients.NotConfigured("researcher", "Researcher profile has no provider.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.DelegateResearch, new { task = "Research X" })
            .ThenText("I answered without the researcher.");

        var events = await host.ChatAsync("X?", cancellationToken: Ct);

        var result = events.OfType<ToolResultEvent>().Single();
        result.IsError.ShouldBeTrue();
        result.Summary.ShouldBe("The researcher agent cannot run: Researcher profile has no provider.");
        orchestrator.Requests[1].Messages[^1].ToolResults().Single().ResultText().ShouldStartWith("Error: ");
        events.Done().FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task Researcher_refusal_is_reported_as_a_tool_error_not_a_turn_error()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.DelegateResearch, new { task = "Research X" })
            .ThenText("Fine.");
        host.ChatClients.Script("researcher").Then((_, _) =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "No.")) { FinishReason = ChatFinishReason.ContentFilter });

        var events = await host.ChatAsync("X?", cancellationToken: Ct);

        events.OfType<ErrorEvent>().ShouldBeEmpty();
        events.OfType<AgentCompletedEvent>().Single(e => e.Agent == "researcher").FinishReason.ShouldBe("refusal");
        var result = events.OfType<ToolResultEvent>().Single();
        result.IsError.ShouldBeTrue();
        result.Summary.ShouldBe($"The researcher agent could not complete the task: {AgentRunner.RefusalMessage}");
        events.Done().FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task Delegate_research_requires_a_task()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.DelegateResearch, new { source_ids = new[] { "x" } })
            .ThenText("ok");

        var events = await host.ChatAsync("X?", cancellationToken: Ct);

        events.OfType<ToolResultEvent>().Single().Summary.ShouldBe("Invalid arguments for delegate_research: 'task' is required.");
        host.Prompts.RendersOf(PromptNames.ResearcherSystem).ShouldBeEmpty();
    }
}
