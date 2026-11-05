using System.Diagnostics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Infrastructure.Telemetry;
using KnowledgeAgent.Tests.Features.Chat.Support;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class TelemetryTests
{
    private const string OuterSourceName = "KnowledgeAgent.Tests.Outer";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_chat_turn_is_a_new_root_span_linked_to_the_caller_with_tool_and_sub_agent_spans()
    {
        using var collector = new ActivityCollector(KnowledgeAgentTelemetry.Agents, OuterSourceName);
        using var outerSource = new ActivitySource(OuterSourceName);
        using var host = new AgentTestHost();
        host.Search.AddHit("Doc", "Passage text.");
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "passage" })
            .ThenToolCall(ToolNames.DelegateResearch, new { task = "Dig deeper" })
            .ThenText("Answer [S1].");
        host.ChatClients.Script("researcher")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "deeper" })
            .ThenText("Report [S1].");

        List<ChatStreamEvent> events;
        ActivityContext outerContext;
        using (var outer = outerSource.StartActivity("POST /api/chat", ActivityKind.Server))
        {
            outer.ShouldNotBeNull();
            outerContext = outer.Context;
            events = await host.ChatAsync("What does the passage say?", cancellationToken: Ct);
            Activity.Current.ShouldBe(outer, "the caller's current activity is untouched");
        }

        var conversationId = events.ConversationId().ToString();
        var turn = collector.Stopped.Single(a =>
            a.OperationName == "chat.turn" && (string?)a.GetTagItem("langfuse.session.id") == conversationId);

        turn.Source.Name.ShouldBe(KnowledgeAgentTelemetry.Agents);
        turn.Parent.ShouldBeNull();
        turn.ParentSpanId.ShouldBe(default);
        turn.TraceId.ShouldNotBe(outerContext.TraceId);
        turn.Links.ShouldContain(link => link.Context.TraceId == outerContext.TraceId && link.Context.SpanId == outerContext.SpanId);
        turn.GetTagItem("langfuse.trace.name").ShouldBe("chat-turn");
        turn.GetTagItem("langfuse.observation.type").ShouldBe("agent");
        turn.GetTagItem("langfuse.trace.input").ShouldBe("What does the passage say?");
        turn.GetTagItem("langfuse.trace.output").ShouldBe("Answer [S1].");
        turn.GetTagItem("gen_ai.agent.name").ShouldBe("orchestrator");
        turn.GetTagItem("gen_ai.request.model").ShouldBe("orchestrator-model");
        turn.GetTagItem("knowledge_agent.finish_reason").ShouldBe("stop");
        turn.Status.ShouldNotBe(ActivityStatusCode.Error);

        var spans = collector.Stopped.Where(a => a.TraceId == turn.TraceId).ToList();
        var orchestratorSearch = spans.Single(a =>
            a.OperationName == "tool search_knowledge_base" && (string?)a.GetTagItem("gen_ai.agent.name") == "orchestrator");
        orchestratorSearch.ParentSpanId.ShouldBe(turn.SpanId);
        orchestratorSearch.GetTagItem("langfuse.observation.type").ShouldBe("tool");
        ((string)orchestratorSearch.GetTagItem("langfuse.observation.input")!).ShouldContain("\"query\":\"passage\"");
        ((string)orchestratorSearch.GetTagItem("langfuse.observation.output")!).ShouldStartWith("Found 1 passage(s)");

        var delegateSpan = spans.Single(a => a.OperationName == "tool delegate_research");
        delegateSpan.ParentSpanId.ShouldBe(turn.SpanId);
        var researcherSpan = spans.Single(a => a.OperationName == "agent researcher");
        researcherSpan.ParentSpanId.ShouldBe(delegateSpan.SpanId);
        researcherSpan.GetTagItem("langfuse.observation.type").ShouldBe("agent");
        researcherSpan.GetTagItem("langfuse.observation.input").ShouldBe("Dig deeper");
        researcherSpan.GetTagItem("langfuse.observation.output").ShouldBe("Report [S1].");
        var researcherSearch = spans.Single(a =>
            a.OperationName == "tool search_knowledge_base" && (string?)a.GetTagItem("gen_ai.agent.name") == "researcher");
        researcherSearch.ParentSpanId.ShouldBe(researcherSpan.SpanId);
    }

    [Fact]
    public async Task Turns_of_one_conversation_share_the_session_but_not_the_trace()
    {
        using var collector = new ActivityCollector(KnowledgeAgentTelemetry.Agents);
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("one").ThenText("two");

        var first = await host.ChatAsync("Hi", cancellationToken: Ct);
        await host.ChatAsync("Again", first.ConversationId(), Ct);

        var conversationId = first.ConversationId().ToString();
        var turns = collector.Stopped
            .Where(a => a.OperationName == "chat.turn" && (string?)a.GetTagItem("langfuse.session.id") == conversationId)
            .ToList();
        turns.Count.ShouldBe(2);
        turns[0].TraceId.ShouldNotBe(turns[1].TraceId);
        turns.ShouldAllBe(a => a.Parent == null);
    }

    [Fact]
    public async Task Failed_turns_and_tools_are_marked_as_errors()
    {
        using var collector = new ActivityCollector(KnowledgeAgentTelemetry.Agents);
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator")
            .ThenToolCall("no_such_tool")
            .ThenThrow(new InvalidOperationException("provider exploded"));

        var events = await host.ChatAsync("Hi", cancellationToken: Ct);

        var conversationId = events.ConversationId().ToString();
        var turn = collector.Stopped.Single(a => a.OperationName == "chat.turn" && (string?)a.GetTagItem("langfuse.session.id") == conversationId);
        turn.Status.ShouldBe(ActivityStatusCode.Error);
        turn.GetTagItem("error.type").ShouldBe("provider_error");
        var tool = collector.Stopped.Single(a => a.TraceId == turn.TraceId && a.OperationName == "tool no_such_tool");
        tool.Status.ShouldBe(ActivityStatusCode.Error);
    }
}
