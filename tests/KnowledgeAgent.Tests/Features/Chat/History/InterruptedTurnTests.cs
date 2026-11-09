using System.Net;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat.History;

public sealed class InterruptedTurnTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Calls_without_a_result_get_one_and_the_turn_ends_with_the_reply()
    {
        IReadOnlyList<ChatMessage> messages =
        [
            new(ChatRole.User, "Cancel order 17"),
            new(ChatRole.Assistant,
            [
                new TextReasoningContent("The person wants order 17 cancelled."),
                new FunctionCallContent("c1", "mcp_shop__cancel_order"),
                new FunctionCallContent("c2", "mcp_shop__get_order"),
            ]),
        ];

        var closed = InterruptedTurn.Close(messages, new HashSet<string> { "c1" }, InterruptedTurn.FailedReply);

        closed.Select(message => message.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);
        closed.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ShouldBeEmpty();
        closed[2].ToolResults().Select(result => (result.CallId, result.ResultText()))
            .ShouldBe([("c1", InterruptedTurn.RanResult), ("c2", InterruptedTurn.MayHaveRunResult)]);
        closed[^1].Text.ShouldBe(InterruptedTurn.FailedReply);
    }

    [Fact]
    public void A_turn_without_messages_stays_empty() =>
        InterruptedTurn.Close([], new HashSet<string>(), InterruptedTurn.FailedReply).ShouldBeEmpty();

    [Fact]
    public async Task After_a_failed_turn_the_next_one_sees_the_tools_that_ran()
    {
        using var host = new AgentTestHost();
        host.Search.AddHit("Refunds", "Refunds take 14 days.");
        var orchestrator = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "refund" })
            .ThenThrow(new HttpRequestException("upstream", null, HttpStatusCode.InternalServerError))
            .ThenText("Refunds take 14 days [S1].");

        var failed = await host.ChatAsync("How long do refunds take?", cancellationToken: Ct);
        failed.Done().FinishReason.ShouldBe(ChatFinishReasons.Error);
        await host.ChatAsync("Try again", failed.ConversationId(), Ct);

        var sent = orchestrator.Requests[^1].Messages.Where(message => message.Role != ChatRole.System).ToList();
        sent.Select(message => message.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User]);
        sent[2].ToolResults().ShouldHaveSingleItem().ResultText().ShouldContain("Refunds take 14 days.");
        sent[3].Text.ShouldBe(InterruptedTurn.FailedReply);
    }

    [Fact]
    public async Task Stopping_while_a_tool_runs_records_that_it_may_have_run()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Search.Handler = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        };
        host.ChatClients.Script("orchestrator").ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "refund" });

        var sending = host.ChatAsync("How long do refunds take?", cancellationToken: Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var conversationId = host.Conversations.All.ShouldHaveSingleItem().Id;
        host.Services.GetRequiredService<ChatTurnHost>().Stop(conversationId).ShouldBeTrue();
        (await sending.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Done().FinishReason.ShouldBe(ChatFinishReasons.Stopped);

        var messages = host.SingleTurn(conversationId).ModelMessages();
        messages.Select(message => message.Role).ShouldBe([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant]);
        messages[2].ToolResults().ShouldHaveSingleItem().ResultText().ShouldBe(InterruptedTurn.MayHaveRunResult);
        messages[3].Text.ShouldBe(InterruptedTurn.StoppedBeforeReply);
    }

    [Fact]
    public void A_stopped_reply_keeps_what_the_person_saw()
    {
        InterruptedTurn.StoppedReply("The Pro plan ").ShouldBe($"The Pro plan\n\n{InterruptedTurn.StoppedNote}");
        InterruptedTurn.StoppedReply(" ").ShouldBe(InterruptedTurn.StoppedBeforeReply);
    }
}
