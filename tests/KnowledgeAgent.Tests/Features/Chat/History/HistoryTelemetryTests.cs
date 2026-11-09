using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Infrastructure.Telemetry;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat.History;

public sealed class HistoryTelemetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_turn_span_carries_cache_tokens_and_the_shape_of_the_replayed_history()
    {
        using var collector = new ActivityCollector(KnowledgeAgentTelemetry.Agents);
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator")
            .Then((_, _) => Cached("One.", read: 0, written: 1_200))
            .Then((_, _) => Cached("Two.", read: 1_200, written: 40));

        var first = await host.ChatAsync("Hi", cancellationToken: Ct);
        await host.ChatAsync("Again", first.ConversationId(), Ct);

        var conversationId = first.ConversationId().ToString();
        var turns = collector.Stopped
            .Where(activity => activity.OperationName == "chat.turn" && (string?)activity.GetTagItem("langfuse.session.id") == conversationId)
            .ToList();
        turns.Count.ShouldBe(2);
        turns[0].GetTagItem("gen_ai.usage.cache_read.input_tokens").ShouldBe(0L);
        turns[0].GetTagItem("gen_ai.usage.cache_creation.input_tokens").ShouldBe(1_200L);
        turns[0].GetTagItem("knowledge_agent.history.turns").ShouldBe(0);
        turns[1].GetTagItem("gen_ai.usage.cache_read.input_tokens").ShouldBe(1_200L);
        turns[1].GetTagItem("gen_ai.usage.cache_creation.input_tokens").ShouldBe(40L);
        turns[1].GetTagItem("knowledge_agent.history.mode").ShouldBe("compact");
        turns[1].GetTagItem("knowledge_agent.history.turns").ShouldBe(1);
        turns[1].GetTagItem("knowledge_agent.history.stubbed_results").ShouldBe(0);
        ((long)turns[1].GetTagItem("knowledge_agent.history.estimated_tokens")!).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Usage_without_cache_counts_leaves_them_unset()
    {
        var usage = new UsageAccumulator();
        usage.Add(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 2 });

        var totals = usage.Snapshot();
        totals.CacheReadInputTokens.ShouldBeNull();
        totals.CacheCreationInputTokens.ShouldBeNull();
        totals.TotalTokens.ShouldBe(12);
    }

    private static ChatResponse Cached(string text, long read, long written) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails
            {
                InputTokenCount = 100 + read + written,
                OutputTokenCount = 5,
                CachedInputTokenCount = read,
                AdditionalCounts = new() { [UsageAccumulator.CacheCreationInputTokensKey] = written },
            },
        };
}
