using System.Runtime.CompilerServices;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class AgentChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_chat_runs_as_the_agent_it_names_for_every_turn()
    {
        using var host = new AgentTestHost();
        var agent = host.Agents.Add(new AgentConfig
        {
            Key = "help-desk",
            Name = "Help desk",
            Instructions = "Answer in two sentences.",
            Language = "German",
            ProfileName = "orchestrator",
            Tools = [ToolNames.SearchKnowledgeBase, ToolNames.DelegateResearch],
            AllowGeneralKnowledge = true,
        });
        var scripted = host.ChatClients.Script("orchestrator").ThenText("Hallo.").ThenText("Gern.");

        var first = await host.ChatAsync(new ChatRequestDto(null, "Hi", Agent: "Help-Desk"), Ct);
        first.Done().FinishReason.ShouldBe("stop");
        var conversationId = first.ConversationId();
        await host.ChatAsync("Thanks", conversationId, Ct);

        host.Conversations.Get(conversationId).AgentId.ShouldBe(agent.Id);
        var variables = host.Prompts.RendersOf(PromptNames.OrchestratorSystem).ShouldHaveSingleItem().Variables;
        variables["agentName"].ShouldBe("Help desk");
        variables["instructions"].ShouldBe("Answer in two sentences.");
        variables["language"].ShouldBe("German");
        variables["allowGeneralKnowledge"].ShouldBe(true);
        variables["researcherEnabled"].ShouldBe(true);
        scripted.Requests.Count.ShouldBe(2);
        scripted.Requests.ShouldAllBe(request => request.Options!.Tools!.Select(tool => tool.Name).SequenceEqual(
            new[] { ToolNames.SearchKnowledgeBase, ToolNames.DelegateResearch }));
    }

    [Fact]
    public async Task Without_an_agent_the_chat_runs_as_the_built_in_one()
    {
        using var host = new AgentTestHost();
        var scripted = host.ChatClients.Script("orchestrator").ThenText("Hello.");

        var events = await host.ChatAsync("Hi", cancellationToken: Ct);

        host.Conversations.Get(events.ConversationId()).AgentId.ShouldBeNull("the built-in agent is not seeded in this host");
        scripted.Requests.ShouldHaveSingleItem().Options!.Tools!.Select(tool => tool.Name)
            .ShouldBe(AgentDefaults.Tools, ignoreOrder: true, "the portal never offers a person");
    }

    [Fact]
    public async Task Unknown_agents_end_the_turn_before_a_conversation_exists()
    {
        using var host = new AgentTestHost();

        var events = await host.ChatAsync(new ChatRequestDto(null, "Hi", Agent: "nobody"), Ct);

        var error = events.ShouldHaveSingleItem().ShouldBeOfType<ErrorEvent>();
        error.Code.ShouldBe(ChatErrorCodes.NotFound);
        error.Message.ShouldContain("nobody");
        host.Conversations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task An_agent_searches_only_the_sources_in_its_folders_and_their_subfolders()
    {
        using var host = new AgentTestHost();
        var support = host.Folders.Add("Support");
        var faq = host.Folders.Add("FAQ", support.Id);
        var other = host.Folders.Add("Finance");
        var inSupport = host.Sources.Add("Returns");
        inSupport.FolderId = support.Id;
        var inFaq = host.Sources.Add("Shipping FAQ");
        inFaq.FolderId = faq.Id;
        var inFinance = host.Sources.Add("Budget");
        inFinance.FolderId = other.Id;
        host.Sources.Add("Unfiled notes");
        host.Agents.Add(new AgentConfig { Key = "support", Name = "Support", ProfileName = "orchestrator", FolderIds = [support.Id], Tools = [ToolNames.SearchKnowledgeBase] });
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "return window" })
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "budget", source_ids = new[] { inFinance.Id, inFaq.Id } })
            .ThenText("Thirty days.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "How long can I return items?", Agent: "support"), Ct);

        events.Done().FinishReason.ShouldBe("stop");
        host.Search.Requests.Count.ShouldBe(2);
        host.Search.Requests[0].SourceIds.ShouldNotBeNull().ShouldBe([inSupport.Id, inFaq.Id], ignoreOrder: true);
        host.Search.Requests[1].SourceIds.ShouldNotBeNull().ShouldBe([inFaq.Id], "the model can narrow the scope but never leave it");
    }

    [Fact]
    public async Task An_agent_whose_folders_hold_no_sources_finds_nothing()
    {
        using var host = new AgentTestHost();
        var empty = host.Folders.Add("Empty");
        host.Sources.Add("Elsewhere");
        host.Agents.Add(new AgentConfig
        {
            Key = "empty",
            Name = "Empty",
            ProfileName = "orchestrator",
            FolderIds = [empty.Id],
            Tools = [ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources],
        });
        host.ChatClients.Script("orchestrator")
            .ThenToolCalls(
                (ToolNames.SearchKnowledgeBase, new { query = "anything" }),
                (ToolNames.GrepSources, new { text = "anything" }),
                (ToolNames.FindSources, new { query = "anything" }))
            .ThenText("Nothing is stored yet.");

        var events = await host.ChatAsync(new ChatRequestDto(null, "What do you know?", Agent: "empty"), Ct);

        var results = events.OfType<ToolResultEvent>().ToList();
        results.Select(result => result.ToolName).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources], ignoreOrder: true);
        results.ShouldAllBe(result => result.Summary == SearchKnowledgeBaseTool.NoSourcesMessage);
        host.Search.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_message_while_the_answer_is_written_is_refused()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) => TextThenRest("Part one", ", part two.", started, release, token)));

        var first = host.ChatAsync("First question", cancellationToken: Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var conversationId = host.Conversations.All.ShouldHaveSingleItem().Id;

        var busy = await host.WithChatServiceAsync(chat =>
            Task.FromResult(Should.Throw<ConversationBusyException>(() => chat.StreamAsync(new ChatRequestDto(conversationId, "Second"), Ct))));
        busy.ConversationId.ShouldBe(conversationId);

        release.SetResult();
        (await first.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Text().ShouldBe("Part one, part two.");
        host.Services.GetRequiredService<ChatTurnHost>().IsActive(conversationId).ShouldBeFalse();
        host.ChatClients.Script("orchestrator").ThenText("Now it works.");
        (await host.ChatAsync("Second", conversationId, Ct)).Text().ShouldBe("Now it works.");
    }

    [Fact]
    public async Task The_next_message_may_follow_as_soon_as_an_answer_ends()
    {
        // The turn's scope is still being disposed when its events end, as with a slow database connection.
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new AgentTestHost(configureServices: services => services.AddScoped(_ => new SlowToDispose(disposing.Task)));
        var turns = host.Services.GetRequiredService<ChatTurnHost>();
        var conversationId = Guid.NewGuid();

        await Drain(turns.Start(
            conversationId,
            (services, broadcast, _) =>
            {
                services.GetRequiredService<SlowToDispose>();
                broadcast.Writer.TryComplete();
                return Task.CompletedTask;
            },
            Ct));

        turns.IsActive(conversationId).ShouldBeFalse("the answer has ended");
        await Drain(turns.Start(conversationId, (_, broadcast, _) => Task.FromResult(broadcast.Writer.TryComplete()), Ct));
        disposing.SetResult();
    }

    [Fact]
    public async Task A_stopped_answer_keeps_its_text_and_stays_out_of_the_next_turns_context()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) => TextThenRest("Part one", " and more", started, never, token)));

        var sending = host.ChatAsync("Explain everything", cancellationToken: Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var conversationId = host.Conversations.All.ShouldHaveSingleItem().Id;
        var turns = host.Services.GetRequiredService<ChatTurnHost>();

        turns.Stop(conversationId).ShouldBeTrue();
        turns.Stop(conversationId).ShouldBeFalse("it is already stopping");
        var events = await sending.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        events.Done().FinishReason.ShouldBe(ChatFinishReasons.Stopped);
        events.OfType<ErrorEvent>().ShouldBeEmpty();
        var turn = host.SingleTurn(conversationId);
        turn.AssistantMessage.ShouldBe("Part one");
        turn.FinishReason.ShouldBe(ChatFinishReasons.Stopped);
        turn.ModelMessages().ShouldBeEmpty();
        host.Conversations.Get(conversationId).Messages.Select(message => (message.Author, message.Text))
            .ShouldBe([(MessageAuthor.User, "Explain everything"), (MessageAuthor.Assistant, "Part one")]);
        turns.IsActive(conversationId).ShouldBeFalse();

        var scripted = host.ChatClients.Script("orchestrator").ThenText("Short version.");
        (await host.ChatAsync("Just the short version", conversationId, Ct)).Text().ShouldBe("Short version.");
        scripted.Requests.ShouldHaveSingleItem().Messages.Where(message => message.Role != ChatRole.System).Select(message => message.Text)
            .ShouldBe(["Just the short version"]);
        turns.Stop(conversationId).ShouldBeFalse("nothing is running");
    }

    [Fact]
    public async Task Status_changes_reach_live_views_spelled_as_the_api_spells_them()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("Hello.");
        var conversationId = (await host.ChatAsync("Hi", cancellationToken: Ct)).ConversationId();
        using var subscription = host.Services.GetRequiredService<ConversationEventHub>().Subscribe(conversationId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ConversationTimeline>().SetStatusAsync(conversationId, ConversationStatus.HandoverPending, Ct);
        }

        subscription.Reader.TryRead(out var liveEvent).ShouldBeTrue();
        liveEvent.ShouldBeOfType<ConversationChangedEvent>().Status.ShouldBe("handover_pending");
        host.Conversations.Get(conversationId).Status.ShouldBe(ConversationStatus.HandoverPending);
    }

    [Fact]
    public async Task Viewers_who_join_mid_turn_get_the_whole_turn_and_every_stored_message()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) => TextThenRest("Hello", " there.", started, release, token)));

        var sender = host.ChatAsync("Greet me", cancellationToken: Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var conversationId = host.Conversations.All.ShouldHaveSingleItem().Id;

        using var subscription = host.Services.GetRequiredService<ConversationEventHub>().Subscribe(conversationId);
        var broadcast = host.Services.GetRequiredService<ChatTurnHost>().Find(conversationId).ShouldNotBeNull();
        var lateReader = ReadAllAsync(broadcast);
        release.SetResult();

        var sent = await sender.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var replayed = await lateReader.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        replayed.Select(e => e.EventType).ShouldBe(sent.Select(e => e.EventType));
        replayed.Text().ShouldBe("Hello there.");

        var live = new List<ConversationLiveEvent>();
        while (subscription.Reader.TryRead(out var liveEvent))
        {
            live.Add(liveEvent);
        }

        live.OfType<TurnStreamEvent>().Select(e => e.Event).OfType<TextDeltaEvent>().Select(e => e.Text).ShouldBe([" there."]);
        live.OfType<MessageAddedEvent>().Select(e => (e.Message.Author, e.Message.Text)).ShouldBe([("assistant", "Hello there.")]);
        host.Conversations.Get(conversationId).Messages.Select(message => message.Sequence).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task Only_widget_callers_that_offer_a_person_give_the_agent_the_handover_tool()
    {
        var handover = new RecordingHandover();
        using var host = new AgentTestHost(configureServices: services => services.AddScoped<IHandoverRequester>(_ => handover));
        host.Agents.Add(new AgentConfig { Key = "site", Name = "Site", ProfileName = "orchestrator", Tools = [ToolNames.SearchKnowledgeBase], HandoverPolicy = HandoverPolicy.WhenUnsure });
        var scripted = host.ChatClients.Script("orchestrator")
            .ThenText("No person here.")
            .ThenToolCall(ToolNames.RequestHumanHandover, new { reason = "asked for a person", summary = "Wants a refund." })
            .ThenText("A colleague will reply here.");

        await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Hi", Agent: "site"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid()),
            Ct)));
        var events = await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "I want a person", Agent: "site"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid(), HandoverAvailable: true),
            Ct)));

        scripted.Requests[0].Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.SearchKnowledgeBase]);
        scripted.Requests[1].Options!.Tools!.Select(tool => tool.Name).ShouldBe([ToolNames.SearchKnowledgeBase, ToolNames.RequestHumanHandover]);
        var renders = host.Prompts.RendersOf(PromptNames.OrchestratorSystem);
        renders[0].Variables["handoverEnabled"].ShouldBe(false);
        renders[0].Variables["offerHandoverWhenUnsure"].ShouldBe(false);
        renders[1].Variables["handoverEnabled"].ShouldBe(true);
        renders[1].Variables["offerHandoverWhenUnsure"].ShouldBe(true);
        renders[1].Variables["isVisitor"].ShouldBe(true);
        handover.Requests.ShouldHaveSingleItem().Summary.ShouldBe("Wants a refund.");
        events.OfType<ToolResultEvent>().ShouldHaveSingleItem().Summary.ShouldBe("A person was asked to join");
        events.Text().ShouldBe("A colleague will reply here.");
    }

    [Theory]
    [InlineData(HandoverPolicy.Never, false, false)]
    [InlineData(HandoverPolicy.OnRequest, true, false)]
    [InlineData(HandoverPolicy.WhenUnsure, true, true)]
    public async Task The_agent_handover_policy_decides_when_it_offers_a_person_in_widgets_that_offer_one(
        HandoverPolicy policy,
        bool getsTool,
        bool offersWhenUnsure)
    {
        using var host = new AgentTestHost();
        host.Agents.Add(new AgentConfig { Key = "site", Name = "Site", ProfileName = "orchestrator", Tools = [ToolNames.SearchKnowledgeBase], HandoverPolicy = policy });
        var scripted = host.ChatClients.Script("orchestrator").ThenText("Hello.");

        await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Hi", Agent: "site"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid(), HandoverAvailable: true),
            Ct)));

        scripted.Requests.ShouldHaveSingleItem().Options!.Tools!.Any(tool => tool.Name == ToolNames.RequestHumanHandover).ShouldBe(getsTool);
        var variables = host.Prompts.RendersOf(PromptNames.OrchestratorSystem).ShouldHaveSingleItem().Variables;
        variables["handoverEnabled"].ShouldBe(getsTool);
        variables["offerHandoverWhenUnsure"].ShouldBe(offersWhenUnsure);
    }

    [Fact]
    public async Task Failed_widget_turns_store_plain_words_for_the_visitor_and_the_full_error_for_staff()
    {
        using var host = new AgentTestHost();
        host.Agents.Add(new AgentConfig { Key = "site", Name = "Site", ProfileName = "orchestrator", Tools = [ToolNames.SearchKnowledgeBase] });
        host.ChatClients.NotConfigured("orchestrator", "No configured chat provider is available for the 'orchestrator' profile.");

        var events = await host.WithChatServiceAsync(async chat => await Drain(chat.StreamAsync(
            new ChatRequestDto(null, "Hi", Agent: "site"),
            new ChatCaller(ConversationChannel.Widget, WidgetId: Guid.NewGuid(), VisitorId: Guid.NewGuid()),
            Ct)));

        events.OfType<ErrorEvent>().ShouldHaveSingleItem().Code.ShouldBe(ChatErrorCodes.ModelNotConfigured);
        var conversation = host.Conversations.Get(events.ConversationId());
        conversation.Messages.Select(message => (message.Author, message.Text)).ShouldBe(
            [(MessageAuthor.Visitor, "Hi"), (MessageAuthor.System, VisitorErrorTexts.Unavailable)]);
        conversation.Turns.ShouldHaveSingleItem().Error.ShouldNotBeNull().ShouldContain("No configured chat provider");
    }

    [Fact]
    public async Task Feedback_told_to_the_agent_is_recorded_for_the_conversation()
    {
        var recorder = new RecordingFeedback();
        using var host = new AgentTestHost(configureServices: services => services.AddScoped<IFeedbackRecorder>(_ => recorder));
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.RecordFeedback, new { sentiment = "negative", comment = "The pricing page is confusing." })
            .ThenText("Thanks, I passed that on.");

        var events = await host.ChatAsync("Feedback: your pricing page is confusing", cancellationToken: Ct);

        var saved = recorder.Saved.ShouldHaveSingleItem();
        saved.ConversationId.ShouldBe(events.ConversationId());
        saved.Sentiment.ShouldBe(-1);
        saved.Comment.ShouldBe("The pricing page is confusing.");
        events.OfType<ToolResultEvent>().ShouldHaveSingleItem().Summary.ShouldBe("Feedback recorded");
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

    private sealed class SlowToDispose(Task disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(disposed);
    }

    private sealed class RecordingHandover : IHandoverRequester
    {
        public List<(Guid ConversationId, string Reason, string Summary)> Requests { get; } = [];

        public Task<HandoverOutcome> RequestFromAgentAsync(Guid conversationId, string reason, string summary, CancellationToken cancellationToken = default)
        {
            Requests.Add((conversationId, reason, summary));
            return Task.FromResult(new HandoverOutcome(true, "A person was asked. Say so."));
        }
    }

    private sealed class RecordingFeedback : IFeedbackRecorder
    {
        public List<(Guid ConversationId, int Sentiment, string Comment)> Saved { get; } = [];

        public Task<KnowledgeAgent.Features.Feedback.Feedback> RecordFromAgentAsync(Guid conversationId, int sentiment, string comment, CancellationToken cancellationToken = default)
        {
            Saved.Add((conversationId, sentiment, comment));
            return Task.FromResult(new KnowledgeAgent.Features.Feedback.Feedback { ConversationId = conversationId, Rating = sentiment, Comment = comment });
        }
    }

    private static async Task<List<ChatStreamEvent>> ReadAllAsync(TurnBroadcast broadcast)
    {
        var events = new List<ChatStreamEvent>();
        await foreach (var streamEvent in broadcast.ReadAllAsync(Ct))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> TextThenRest(
        string first,
        string rest,
        TaskCompletionSource started,
        TaskCompletionSource release,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, first) { MessageId = "m1" };
        started.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, rest) { MessageId = "m1" };
    }
}
