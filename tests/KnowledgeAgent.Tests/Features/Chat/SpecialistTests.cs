using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class SpecialistTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_front_agent_hands_off_to_a_specialist_that_answers_in_the_same_turn_and_the_next_until_it_hands_back()
    {
        using var host = new AgentTestHost();
        var (front, billing) = AddTeam(host, showNames: true);
        var frontModel = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.TransferToAgent, new { agent = "billing", reason = "question about an invoice" })
            .ThenText("Orders ship in two days.");
        var billingModel = host.ChatClients.Script("billing")
            .ThenText("Invoice 42 is paid.")
            .ThenToolCall(ToolNames.TransferBack, new { reason = "asks about shipping now" });

        var first = await host.ChatAsync(new ChatRequestDto(null, "Is invoice 42 paid?", Agent: "front"), Ct);

        first.Done().FinishReason.ShouldBe("stop");
        var transfer = first.OfType<AgentTransferEvent>().ShouldHaveSingleItem();
        (transfer.ToAgentId, transfer.ToAgent, transfer.ToAgentName, transfer.Reason, transfer.Back, transfer.Agent)
            .ShouldBe((billing.Id, "billing", "Billing assistant", "question about an invoice", false, "orchestrator"));
        first.Text("billing").ShouldBe("Invoice 42 is paid.");
        var conversationId = first.ConversationId();
        var conversation = host.Conversations.Get(conversationId);
        conversation.AgentId.ShouldBe(front.Id, "the front agent stays the conversation's agent");
        conversation.ActiveAgentId.ShouldBe(billing.Id);

        frontModel.Requests[0].Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.TransferToAgent]);
        billingModel.Requests[0].Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.ReadSource, ToolNames.TransferBack], "a specialist only hands back");
        billingModel.Requests[0].Messages.ShouldContain(message => message.Role == ChatRole.User && message.Text == "Is invoice 42 paid?");
        billingModel.Requests[0].Messages[0].Text.ShouldContain("Billing assistant", Case.Sensitive, "the specialist answers with its own prompt");
        var specialistPrompt = host.Prompts.RendersOf(PromptNames.OrchestratorSystem)[1].Variables;
        specialistPrompt["instructions"].ShouldBe("Only invoices and refunds.");
        specialistPrompt["handBackEnabled"].ShouldBe(true);
        specialistPrompt["frontAgentName"].ShouldBe("Front desk");
        host.Prompts.RendersOf(PromptNames.OrchestratorSystem)[0].Variables["specialistsEnabled"].ShouldBe(true);

        var turn = host.SingleTurn(conversationId);
        turn.AssistantMessage.ShouldBe("Invoice 42 is paid.");
        turn.ToolCalls().Select(call => (call.ToolName, call.Agent)).ShouldBe([(ToolNames.TransferToAgent, "orchestrator")]);

        var second = await host.ChatAsync("And when does my order ship?", conversationId, Ct);

        second.OfType<AgentTransferEvent>().ShouldHaveSingleItem().Back.ShouldBeTrue();
        second.Text().ShouldBe("Orders ship in two days.");
        billingModel.Requests.Count.ShouldBe(2, "the specialist answers the next turn first");
        frontModel.Requests[1].Messages[0].Text.ShouldNotContain("Billing assistant", Case.Sensitive, "the front agent answers with its own prompt again");
        conversation = host.Conversations.Get(conversationId);
        conversation.ActiveAgentId.ShouldBeNull();
        conversation.Messages.Where(message => message.Author == MessageAuthor.System).Select(message => (message.Text, message.StaffOnly))
            .ShouldBe([("Billing assistant joined", false), ("Front desk took over again", false)]);
        conversation.Messages.Where(message => message.Author == MessageAuthor.Assistant).Select(message => message.AuthorName)
            .ShouldBe(["Billing assistant", null]);
    }

    [Fact]
    public async Task With_hidden_names_the_specialist_speaks_as_the_front_agent_and_the_notice_is_for_staff_only()
    {
        using var host = new AgentTestHost();
        var (_, billing) = AddTeam(host, showNames: false);
        host.ChatClients.Script("orchestrator").ThenToolCall(ToolNames.TransferToAgent, new { agent = "billing", reason = "refund" });
        host.ChatClients.Script("billing").ThenText("Refunds take five days.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "Where is my refund?", Agent: "front"), Ct);

        host.Prompts.RendersOf(PromptNames.OrchestratorSystem)[1].Variables["agentName"].ShouldBe("Front desk");
        var conversationId = events.ConversationId();
        var notice = host.Conversations.Get(conversationId).Messages.Single(message => message.Author == MessageAuthor.System);
        notice.StaffOnly.ShouldBeTrue();
        notice.Text.ShouldContain("Billing assistant");

        var dto = await host.WithChatServiceAsync(chat => chat.GetConversationAsync(conversationId, Ct));
        dto.ShouldNotBeNull().ActiveAgent.ShouldBe(new ConversationActiveAgentDto(billing.Id, "Billing assistant"));
        dto.Messages.ShouldNotBeNull().ShouldContain(message => message.Author == "system" && message.StaffOnly);

        var widget = new Widget { Id = Guid.NewGuid(), Name = "Shop" };
        VisitorService.ToVisitorDto(notice.ToDto(), widget).ShouldBeNull("visitors see one assistant");
    }

    [Fact]
    public async Task A_specialist_searches_only_its_own_scope_and_uses_its_own_tools()
    {
        using var host = new AgentTestHost();
        var invoices = host.Folders.Add("Invoices");
        var invoiceSource = host.Sources.Add("Invoice rules");
        invoiceSource.FolderId = invoices.Id;
        host.Sources.Add("Shipping");
        var billing = host.Agents.Add(new AgentConfig
        {
            Key = "billing",
            Name = "Billing assistant",
            ProfileName = "billing",
            FolderIds = [invoices.Id],
            Tools = [ToolNames.SearchKnowledgeBase],
        });
        host.Agents.Add(new AgentConfig
        {
            Key = "front",
            Name = "Front desk",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase, ToolNames.FindSources],
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices" }],
        });
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "anything" })
            .ThenToolCall(ToolNames.TransferToAgent, new { agent = "billing", reason = "invoice" });
        var billingModel = host.ChatClients.Script("billing")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "invoice" })
            .ThenText("Done.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "Invoice?", Agent: "front"), Ct);

        events.Done().FinishReason.ShouldBe("stop");
        host.Search.Requests.Count.ShouldBe(2);
        (host.Search.Requests[0].SourceIds ?? []).ShouldBeEmpty("the front agent searches every source");
        host.Search.Requests[1].SourceIds.ShouldNotBeNull().ShouldBe([invoiceSource.Id]);
        billingModel.Requests[0].Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.TransferBack]);
        var calls = host.SingleTurn(events.ConversationId()).ToolCalls();
        calls.Select(call => (call.ToolName, call.Agent)).ShouldBe(
        [
            (ToolNames.SearchKnowledgeBase, "orchestrator"),
            (ToolNames.TransferToAgent, "orchestrator"),
            (ToolNames.SearchKnowledgeBase, "billing"),
        ]);
    }

    [Theory]
    [InlineData(false, HandoverPolicy.OnRequest, false)]
    [InlineData(true, HandoverPolicy.OnRequest, true)]
    [InlineData(true, HandoverPolicy.Never, false)]
    public async Task The_widget_still_decides_whether_a_specialist_can_offer_a_person(bool widgetOffersPerson, HandoverPolicy policy, bool getsTool)
    {
        using var host = new AgentTestHost();
        var billing = host.Agents.Add(new AgentConfig { Key = "billing", Name = "Billing", ProfileName = "billing", Tools = [], HandoverPolicy = policy });
        host.Agents.Add(new AgentConfig
        {
            Key = "front",
            Name = "Front desk",
            ProfileName = "orchestrator",
            Tools = [],
            HandoverPolicy = HandoverPolicy.Never,
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices" }],
        });
        host.ChatClients.Script("orchestrator").ThenToolCall(ToolNames.TransferToAgent, new { agent = "billing", reason = "invoice" });
        var billingModel = host.ChatClients.Script("billing").ThenText("Hello.");

        await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Invoice?", Agent: "front"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid(), HandoverAvailable: widgetOffersPerson),
            Ct)));

        billingModel.Requests.ShouldHaveSingleItem().Options!.Tools!.Any(tool => tool.Name == ToolNames.RequestHumanHandover).ShouldBe(getsTool);
    }

    [Fact]
    public async Task Hand_off_events_never_reach_visitors()
    {
        var call = new ToolCallEvent("call-1", ToolNames.TransferToAgent, null);
        var result = new ToolResultEvent("call-1", ToolNames.TransferBack, false, "Handed back", 1, []);
        var transfer = new AgentTransferEvent("call-1", Guid.NewGuid(), "billing", "Billing", "invoice", Back: false);

        VisitorChatEvents.ToVisitor(call, "links").ShouldBeNull();
        VisitorChatEvents.ToVisitor(result, "links").ShouldBeNull();
        VisitorChatEvents.ToVisitor(transfer, "links").ShouldBeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_specialist_the_front_agent_no_longer_lists_hands_the_conversation_back_quietly()
    {
        using var host = new AgentTestHost();
        var (front, _) = AddTeam(host, showNames: true);
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.TransferToAgent, new { agent = "billing", reason = "invoice" })
            .ThenText("I can help with that.");
        var billingModel = host.ChatClients.Script("billing").ThenText("Paid.");
        var conversationId = (await host.ChatAsync(new ChatRequestDto(null, "Invoice?", Agent: "front"), Ct)).ConversationId();

        front.Specialists = [];
        await host.Agents.UpdateAsync(front, Ct);
        var events = await host.ChatAsync("Anything else?", conversationId, Ct);

        events.Text().ShouldBe("I can help with that.");
        billingModel.Requests.Count.ShouldBe(1);
        host.Conversations.Get(conversationId).ActiveAgentId.ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_specialists_are_refused_and_the_front_agent_answers_itself()
    {
        using var host = new AgentTestHost();
        AddTeam(host, showNames: true);
        var frontModel = host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.TransferToAgent, new { agent = "orders", reason = "order" })
            .ThenText("I will answer myself.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "Order?", Agent: "front"), Ct);

        events.OfType<AgentTransferEvent>().ShouldBeEmpty();
        events.OfType<ToolResultEvent>().ShouldHaveSingleItem().IsError.ShouldBeTrue();
        events.Text().ShouldBe("I will answer myself.");
        frontModel.Requests.Count.ShouldBe(2);
        host.Conversations.Get(events.ConversationId()).ActiveAgentId.ShouldBeNull();
    }

    private static (AgentConfig Front, AgentConfig Billing) AddTeam(AgentTestHost host, bool showNames)
    {
        var billing = host.Agents.Add(new AgentConfig
        {
            Key = "billing",
            Name = "Billing assistant",
            Instructions = "Only invoices and refunds.",
            ProfileName = "billing",
            Tools = [ToolNames.ReadSource],
        });
        var front = host.Agents.Add(new AgentConfig
        {
            Key = "front",
            Name = "Front desk",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase],
            Specialists = [new AgentSpecialist { AgentId = billing.Id, WhenToUse = "Invoices, payments and refunds" }],
            ShowSpecialistName = showNames,
        });
        return (front, billing);
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
}
