using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class FinalAnswerTests
{
    private const string Note = "I'll check the pricing guide.";
    private const string Answer = "The Pro plan costs 10 EUR per month [S1].";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_last_step_is_stored_as_the_answer_and_the_note_stays_on_its_tool_call()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall(Note, ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText(Answer);

        var events = await host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);

        var steps = events.OfType<StepFinishedEvent>().ToList();
        steps.Select(step => (step.Step, step.Kind)).ShouldBe([(1, StepFinishedEvent.Interim), (2, StepFinishedEvent.Final)]);
        var types = events.Select(e => e.EventType).ToList();
        types.IndexOf(ChatEventTypes.StepFinished).ShouldBeLessThan(types.IndexOf(ChatEventTypes.ToolCall), "the note becomes a status line before the tool runs");
        events.OfType<TextDeltaEvent>().Where(delta => delta.Step == 1).Select(delta => delta.Text).ShouldBe([Note[..12], Note[12..24], Note[24..]]);
        string.Concat(events.OfType<TextDeltaEvent>().Where(delta => delta.Step == 2).Select(delta => delta.Text)).ShouldBe(Answer);

        var conversationId = events.ConversationId();
        var turn = host.SingleTurn(conversationId);
        turn.AssistantMessage.ShouldBe(Answer);
        turn.ToolCalls().ShouldHaveSingleItem().Preamble.ShouldBe(Note);
        host.Conversations.Get(conversationId).Messages.Where(message => message.Author == MessageAuthor.Assistant).Select(message => message.Text)
            .ShouldBe([Answer]);

        // The model still sees what it wrote.
        turn.ModelMessages().First(message => message.Role == ChatRole.Assistant).Text.ShouldBe(Note);
    }

    [Fact]
    public async Task A_citation_the_note_already_used_is_kept_with_the_answer()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenTextAndToolCall("The guide says 10 EUR [S1]; checking discounts too.", ToolNames.SearchKnowledgeBase, new { query = "discounts" })
            .ThenText(Answer);

        var events = await host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);

        var conversationId = events.ConversationId();
        var turn = host.SingleTurn(conversationId);
        turn.AssistantMessage.ShouldBe(Answer);
        ChatJson.DeserializeDtos<CitationDto>(turn.CitationsJson).Select(citation => citation.Ref).ShouldBe(["S1"]);
        var stored = host.Conversations.Get(conversationId).Messages.Single(message => message.Author == MessageAuthor.Assistant);
        ChatJson.DeserializeDtos<CitationDto>(stored.CitationsJson).Select(citation => citation.Ref).ShouldBe(["S1"]);
        turn.ToolCalls().Select(call => call.Preamble).ShouldBe([null, "The guide says 10 EUR [S1]; checking discounts too."]);
    }

    [Fact]
    public async Task After_a_hand_off_the_specialists_answer_replaces_the_front_agents_note()
    {
        using var host = new AgentTestHost();
        var billing = host.Agents.Add(new AgentConfig { Key = "billing", Name = "Billing assistant", ProfileName = "billing", Tools = [ToolNames.ReadSource] });
        host.Agents.Add(new AgentConfig
        {
            Key = "front",
            Name = "Front desk",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase],
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices" }],
            ShowSpecialistName = true,
        });
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall("Let me get our billing team.", ToolNames.TransferToAgent, new { agent = "billing", reason = "invoice" });
        host.ChatClients.Script("billing").ThenText("Invoice 42 is paid.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "Is invoice 42 paid?", Agent: "front"), Ct);

        events.OfType<StepFinishedEvent>().Select(step => (step.Agent, step.Step, step.Kind))
            .ShouldBe([("orchestrator", 1, StepFinishedEvent.Interim), ("billing", 2, StepFinishedEvent.Final)]);
        var visible = events.Select(e => VisitorChatEvents.ToVisitor(e, WidgetChoices.CitationsLinks)).OfType<ChatStreamEvent>().ToList();
        visible.OfType<StepFinishedEvent>().Count().ShouldBe(2, "a visitor never sees the transfer call, so this is how the widget learns the note was not the answer");

        var conversationId = events.ConversationId();
        var turn = host.SingleTurn(conversationId);
        turn.AssistantMessage.ShouldBe("Invoice 42 is paid.");
        turn.ToolCalls().ShouldHaveSingleItem().Preamble.ShouldBe("Let me get our billing team.");
        host.Conversations.Get(conversationId).Messages.Where(message => message.Author == MessageAuthor.Assistant)
            .Select(message => (message.AuthorName, message.Text))
            .ShouldBe([("Billing assistant", "Invoice 42 is paid.")]);
    }

    [Fact]
    public async Task An_empty_last_step_falls_back_to_the_last_note()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall(Answer, ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText(string.Empty);

        var events = await host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);

        events.Done().FinishReason.ShouldBe(ChatFinishReasons.Stop);
        host.SingleTurn(events.ConversationId()).AssistantMessage.ShouldBe(Answer);
    }

    [Fact]
    public async Task With_answer_text_all_every_step_is_joined_as_before()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:AnswerText"] = "all" });
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall(Note, ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText(Answer);

        var events = await host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);

        events.OfType<StepFinishedEvent>().ShouldBeEmpty();
        var turn = host.SingleTurn(events.ConversationId());
        turn.AssistantMessage.ShouldBe($"{Note}\n\n{Answer}");
        turn.ToolCalls().ShouldHaveSingleItem().Preamble.ShouldBe(Note);
    }

    [Fact]
    public async Task Stopping_while_a_tool_runs_after_a_note_stores_no_answer()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) =>
            Interlocked.Increment(ref calls) == 1 ? NoteAndSearch() : Wait(started, firstText: null, token)));

        var (events, turn, conversationId) = await StopAsync(host, started);

        events.OfType<StepFinishedEvent>().ShouldHaveSingleItem().Kind.ShouldBe(StepFinishedEvent.Interim);
        turn.AssistantMessage.ShouldBe(string.Empty);
        turn.ToolCalls().ShouldHaveSingleItem().Preamble.ShouldBe(Note);
        host.Conversations.Get(conversationId).Messages.ShouldNotContain(message => message.Author == MessageAuthor.Assistant);
    }

    [Fact]
    public async Task Stopping_while_the_answer_is_written_keeps_only_its_text()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) =>
            Interlocked.Increment(ref calls) == 1 ? NoteAndSearch() : Wait(started, firstText: "The Pro plan", token)));

        var (_, turn, _) = await StopAsync(host, started);

        turn.AssistantMessage.ShouldBe("The Pro plan");
    }

    [Fact]
    public async Task The_eval_capture_scores_the_stored_answer()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall(Note, ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText(Answer);

        var result = await AgentTurnCapture.RunAsync(host.Services, "How much is the Pro plan?", profile: null, Ct);

        result.Answer.ShouldBe(Answer);
        result.Answer.ShouldBe(host.SingleTurn(result.ConversationId!.Value).AssistantMessage);
        result.Available.Keys.ShouldBe(["S1"]);
    }

    [Fact]
    public async Task The_eval_capture_falls_back_to_the_last_note_like_the_stored_answer()
    {
        using var host = new AgentTestHost();
        AddPricing(host);
        host.ChatClients.Script("orchestrator")
            .ThenTextAndToolCall(Answer, ToolNames.SearchKnowledgeBase, new { query = "pro plan price" })
            .ThenText(string.Empty);

        var result = await AgentTurnCapture.RunAsync(host.Services, "How much is the Pro plan?", profile: null, Ct);

        result.Answer.ShouldBe(Answer);
        result.Answer.ShouldBe(host.SingleTurn(result.ConversationId!.Value).AssistantMessage);
    }

    [Fact]
    public void Visitors_get_the_step_events_of_the_answering_agent_only()
    {
        var step = new StepFinishedEvent(3, StepFinishedEvent.Interim);
        VisitorChatEvents.ToVisitor(step, WidgetChoices.CitationsLinks).ShouldBe(step);
        VisitorChatEvents.ToVisitor(step with { Agent = "researcher", ParentCallId = "c1" }, WidgetChoices.CitationsLinks).ShouldBeNull();
    }

    [Fact]
    public async Task The_step_event_goes_out_with_its_number_and_kind()
    {
        var pipe = new Pipe();
        ChatEventStreamResult.WriteEvent(pipe.Writer, new StepFinishedEvent(2, StepFinishedEvent.Interim), JsonSerializerOptions.Web);
        ChatEventStreamResult.WriteEvent(pipe.Writer, new TextDeltaEvent("Hi") { Step = 3 }, JsonSerializerOptions.Web);
        await pipe.Writer.CompleteAsync();

        var read = await pipe.Reader.ReadAsync(Ct);
        var text = Encoding.UTF8.GetString(read.Buffer);
        text.ShouldStartWith("event: step-finished\ndata: {");
        text.ShouldContain("\"step\":2");
        text.ShouldContain("\"kind\":\"interim\"");
        text.ShouldContain("event: text-delta\ndata: {\"text\":\"Hi\"");
        text.ShouldContain("\"step\":3");
    }

    private static void AddPricing(AgentTestHost host) =>
        host.Search.AddHit("Pricing Guide", "The Pro plan costs 10 EUR per month.", score: 0.87, page: 3, url: "https://example.com/pricing");

    private static async Task<(List<ChatStreamEvent> Events, ConversationTurn Turn, Guid ConversationId)> StopAsync(AgentTestHost host, TaskCompletionSource started)
    {
        var sending = host.ChatAsync("How much is the Pro plan?", cancellationToken: Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var conversationId = host.Conversations.All.ShouldHaveSingleItem().Id;
        host.Services.GetRequiredService<ChatTurnHost>().Stop(conversationId).ShouldBeTrue();
        var events = await sending.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        events.Done().FinishReason.ShouldBe(ChatFinishReasons.Stopped);
        return (events, host.SingleTurn(conversationId), conversationId);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> NoteAndSearch()
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Note) { MessageId = "m1" };
        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [new FunctionCallContent("call_1", ToolNames.SearchKnowledgeBase, new Dictionary<string, object?> { ["query"] = "pro plan price" })])
        {
            MessageId = "m1",
            FinishReason = ChatFinishReason.ToolCalls,
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Wait(
        TaskCompletionSource started,
        string? firstText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (firstText is not null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, firstText) { MessageId = "m2" };
        }

        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "never") { MessageId = "m2" };
    }
}
