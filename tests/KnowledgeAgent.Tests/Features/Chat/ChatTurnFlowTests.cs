using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class ChatTurnFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tool_call_then_answer_streams_events_in_order_and_persists_the_turn()
    {
        using var host = new AgentTestHost();
        var hit = host.Search.AddHit("Pricing Guide", "The Pro plan costs 10 EUR per month.", score: 0.87, page: 3, url: "https://example.com/pricing");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText("The Pro plan costs 10 EUR per month [S1].");
        host.ChatClients.Script("utility").ThenText("  \"Pro plan pricing.\"  ");

        var events = await host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);

        events.CollapsedTypes().ShouldBe(
        [
            ChatEventTypes.Conversation,
            ChatEventTypes.AgentStarted,
            ChatEventTypes.StepFinished,
            ChatEventTypes.ToolCall,
            ChatEventTypes.ToolResult,
            ChatEventTypes.TextDelta,
            ChatEventTypes.StepFinished,
            ChatEventTypes.Usage,
            ChatEventTypes.AgentCompleted,
            ChatEventTypes.Conversation,
            ChatEventTypes.Done,
        ]);
        events.ShouldAllBe(e => e.Agent == "orchestrator" && e.ParentCallId == null);
        events.OfType<TextDeltaEvent>().Count().ShouldBeGreaterThan(1);
        events.Text().ShouldBe("The Pro plan costs 10 EUR per month [S1].");

        var first = events.OfType<ConversationEvent>().First();
        first.IsNew.ShouldBeTrue();
        first.Title.ShouldBe("New conversation");

        var started = events.OfType<AgentStartedEvent>().Single();
        started.Provider.ShouldBe("fake");
        started.Model.ShouldBe("orchestrator-model");

        var toolCall = events.OfType<ToolCallEvent>().Single();
        toolCall.ToolName.ShouldBe(ToolNames.SearchKnowledgeBase);
        toolCall.Arguments!.Value.GetProperty("query").GetString().ShouldBe("pro plan price");

        var toolResult = events.OfType<ToolResultEvent>().Single();
        toolResult.CallId.ShouldBe(toolCall.CallId);
        toolResult.IsError.ShouldBeFalse();
        toolResult.Summary.ShouldBe("1 passage for \"pro plan price\"");
        var citation = toolResult.Citations.ShouldHaveSingleItem();
        citation.ShouldBe(new CitationDto("S1", hit.SourceId, hit.ChunkId, "Pricing Guide", "https://example.com/pricing", 3, "The Pro plan costs 10 EUR per month.", 0.87));

        var usage = events.OfType<UsageEvent>().Single();
        usage.Provider.ShouldBe("fake");
        usage.InputTokens.ShouldNotBeNull().ShouldBeGreaterThan(0);
        usage.TotalTokens.ShouldBe(usage.InputTokens + usage.OutputTokens);

        var completed = events.OfType<AgentCompletedEvent>().Single();
        completed.FinishReason.ShouldBe("stop");
        completed.Iterations.ShouldBe(2);

        var titled = events.OfType<ConversationEvent>().Last();
        titled.ConversationId.ShouldBe(first.ConversationId);
        titled.Title.ShouldBe("Pro plan pricing");

        var done = events.Done();
        done.ConversationId.ShouldBe(first.ConversationId);
        done.FinishReason.ShouldBe("stop");

        orchestrator.Requests.Count.ShouldBe(2);
        var toolMessage = orchestrator.Requests[1].Messages[^1];
        toolMessage.Role.ShouldBe(ChatRole.Tool);
        toolMessage.ToolResults().ShouldHaveSingleItem().ResultText().Unwrapped().ShouldStartWith("Found 1 passage(s) for \"pro plan price\".");

        var conversation = host.Conversations.Get(first.ConversationId);
        conversation.Title.ShouldBe("Pro plan pricing");
        conversation.SystemPrompt.ShouldBe("You are Knowledge Agent (Knowledge Agent). Today is 2026-10-22. Ready sources: 0.");
        conversation.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        conversation.PromptVersion.ShouldBe(3);
        conversation.PromptOrigin.ShouldBe("langfuse");
        conversation.Profile.ShouldBe("orchestrator");

        var turn = conversation.Turns.ShouldHaveSingleItem();
        turn.Id.ShouldBe(done.TurnId);
        turn.Sequence.ShouldBe(1);
        turn.UserMessage.ShouldBe("How much is the Pro plan?");
        turn.AssistantMessage.ShouldBe("The Pro plan costs 10 EUR per month [S1].");
        turn.Provider.ShouldBe("fake");
        turn.Model.ShouldBe("orchestrator-model");
        turn.FinishReason.ShouldBe("stop");
        turn.Error.ShouldBeNull();
        turn.InputTokens.ShouldBe(usage.InputTokens);
        turn.OutputTokens.ShouldBe(usage.OutputTokens);
        turn.CreatedAt.ShouldBe(AgentTestHost.Now);

        var messages = turn.ModelMessages();
        messages.Select(m => m.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);
        messages[1].Contents.OfType<FunctionCallContent>().Single().CallId.ShouldBe(toolCall.CallId);
        messages[2].ToolResults().Single().CallId.ShouldBe(toolCall.CallId);
        messages[3].Text.ShouldBe("The Pro plan costs 10 EUR per month [S1].");

        var loggedCall = turn.ToolCalls().ShouldHaveSingleItem();
        loggedCall.CallId.ShouldBe(toolCall.CallId);
        loggedCall.Agent.ShouldBe("orchestrator");
        loggedCall.ParentCallId.ShouldBeNull();
        loggedCall.Summary.ShouldBe(toolResult.Summary);
        loggedCall.Arguments!.Value.GetProperty("query").GetString().ShouldBe("pro plan price");

        turn.Citations().ShouldBe([citation]);
    }

    [Fact]
    public async Task Answer_without_tools_streams_reasoning_and_text()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("Hello! Ask me about your documents.", reasoning: "The user greets me.");

        var events = await host.ChatAsync("Hi", cancellationToken: Ct);

        events.OfType<ReasoningDeltaEvent>().ShouldHaveSingleItem().Text.ShouldBe("The user greets me.");
        events.Text().ShouldBe("Hello! Ask me about your documents.");
        events.OfType<AgentCompletedEvent>().Single().Iterations.ShouldBe(1);

        var reasoning = host.SingleTurn(events.ConversationId()).ModelMessages()[1].Contents.OfType<TextReasoningContent>().ShouldHaveSingleItem();
        reasoning.Text.ShouldBe("The user greets me.");
        reasoning.ProtectedData.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Offers_the_orchestrator_tools_with_explicit_schemas_and_langfuse_prompt_properties()
    {
        using var host = new AgentTestHost();
        host.Prompts.SetTemplate(PromptNames.OrchestratorSystem, "System v7", version: 7);
        var orchestrator = host.ChatClients.Script("orchestrator").ThenText("Done.");
        var utility = host.ChatClients.Script("utility").ThenText("A title");

        await host.ChatAsync("Question", cancellationToken: Ct);

        var options = orchestrator.Requests.Single().Options.ShouldNotBeNull();
        options.ModelId.ShouldBe("orchestrator-model");
        options.Tools.ShouldNotBeNull().Select(t => t.Name).ShouldBe(
            [ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource, ToolNames.DelegateResearch, ToolNames.RecordFeedback]);
        options.Tools.ShouldAllBe(tool => tool is AIFunctionDeclaration && !(tool is AIFunction));
        options.ToolMode.ShouldBe(ChatToolMode.Auto);
        options.AllowMultipleToolCalls.ShouldBe(true);
        options.AdditionalProperties.ShouldNotBeNull();
        options.AdditionalProperties[AgentRunner.PromptNameProperty].ShouldBe(PromptNames.OrchestratorSystem);
        options.AdditionalProperties[AgentRunner.PromptVersionProperty].ShouldBeOfType<int>().ShouldBe(7);

        var search = (AIFunctionDeclaration)options.Tools[0];
        search.JsonSchema.GetProperty("required")[0].GetString().ShouldBe("query");
        search.JsonSchema.GetProperty("properties").GetProperty("top_k").GetProperty("maximum").GetInt32().ShouldBe(20);
        var grep = (AIFunctionDeclaration)options.Tools[1];
        grep.JsonSchema.GetProperty("required")[0].GetString().ShouldBe("text");
        var read = (AIFunctionDeclaration)options.Tools[3];
        read.JsonSchema.GetProperty("required")[0].GetString().ShouldBe("source_id");

        var titleOptions = utility.Requests.Single().Options.ShouldNotBeNull();
        titleOptions.Tools.ShouldBeNull();
        titleOptions.AdditionalProperties![AgentRunner.PromptNameProperty].ShouldBe(PromptNames.ConversationTitle);
        titleOptions.AdditionalProperties[AgentRunner.PromptVersionProperty].ShouldBe(1);
    }

    [Fact]
    public async Task Researcher_tool_is_not_offered_when_disabled()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:ResearcherEnabled"] = "false" });
        var orchestrator = host.ChatClients.Script("orchestrator").ThenText("Done.");

        await host.ChatAsync("Question", cancellationToken: Ct);

        orchestrator.Requests.Single().Options!.Tools!.Select(t => t.Name)
            .ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource, ToolNames.RecordFeedback]);
    }

    [Fact]
    public async Task Works_end_to_end_with_the_rule_based_model()
    {
        using var host = new AgentTestHost();
        host.Search.AddHit("Handbook", "Employees get 25 vacation days per year.\nMore text.", score: 0.8);
        host.ChatClients.Use("orchestrator", new RuleBasedChatClient());
        host.ChatClients.Use("utility", new RuleBasedChatClient());

        var events = await host.ChatAsync("How many vacation days do employees get?", cancellationToken: Ct);

        events.Text().ShouldBe("According to the knowledge base, Employees get 25 vacation days per year. [S1]");
        events.OfType<ConversationEvent>().Last().Title.ShouldBe("How many vacation days do");
        events.Done().FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task The_first_answer_runs_the_first_answer_hooks_once()
    {
        var hook = new RecordingHook();
        using var host = new AgentTestHost(configureServices: services => services.AddSingleton<IFirstAnswerHook>(hook));
        host.ChatClients.Use("orchestrator", new RuleBasedChatClient());
        host.ChatClients.Use("utility", new RuleBasedChatClient());

        var first = await host.ChatAsync("Where is the handbook?", cancellationToken: Ct);
        await host.ChatAsync("And the holidays?", first.Done().ConversationId, Ct);

        hook.Conversations.ShouldBe([first.Done().ConversationId]);
    }

    private sealed class RecordingHook : IFirstAnswerHook
    {
        public List<Guid> Conversations { get; } = [];

        public Task OnFirstAnswerAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            Conversations.Add(conversation.Id);
            return Task.CompletedTask;
        }
    }
}
