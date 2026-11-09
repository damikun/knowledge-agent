using System.Runtime.CompilerServices;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.Tests.Features.Chat.Support;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class ChatServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Empty_messages_are_rejected_before_streaming(string message)
    {
        using var host = new AgentTestHost();

        var exception = await host.WithChatServiceAsync(chat =>
            Task.FromResult(Should.Throw<ValidationFailedException>(() => chat.StreamAsync(new ChatRequestDto(null, message), Ct))));

        exception.Errors.Keys.ShouldBe(["message"]);
        host.Conversations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Too_long_messages_are_rejected()
    {
        using var host = new AgentTestHost();

        var exception = await host.WithChatServiceAsync(chat =>
            Task.FromResult(Should.Throw<ValidationFailedException>(() => chat.StreamAsync(new ChatRequestDto(null, new string('a', 32_001)), Ct))));

        exception.Errors["message"].ShouldHaveSingleItem().ShouldContain("32,000");
    }

    [Fact]
    public async Task Messages_at_the_limit_are_accepted()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("ok");

        var events = await host.ChatAsync(new string('a', 32_000), cancellationToken: Ct);

        events.Done().FinishReason.ShouldBe("stop");
    }

    [Fact]
    public async Task A_client_that_disconnects_does_not_lose_the_answer()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) => TextThenRest("Partial", " answer.", started, release, token)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var reading = host.WithChatServiceAsync(async service =>
        {
            var received = new List<ChatStreamEvent>();
            try
            {
                await foreach (var streamEvent in service.StreamAsync(new ChatRequestDto(null, "Question"), cancellation.Token))
                {
                    received.Add(streamEvent);
                }
            }
            catch (OperationCanceledException)
            {
            }

            return received;
        });

        // The client leaves while the model is still writing; the turn goes on without it.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cancellation.CancelAsync();
        var events = await reading.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        events.OfType<DoneEvent>().ShouldBeEmpty();
        release.SetResult();

        var conversationId = events.ConversationId();
        var turn = await WaitForTurnAsync(host, conversationId);
        turn.AssistantMessage.ShouldBe("Partial answer.");
        host.Conversations.Get(conversationId).Messages.Select(message => (message.Author, message.Text))
            .ShouldBe([(MessageAuthor.User, "Question"), (MessageAuthor.Assistant, "Partial answer.")]);
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

    private static async Task<ConversationTurn> WaitForTurnAsync(AgentTestHost host, Guid conversationId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (host.Conversations.Get(conversationId).Turns.SingleOrDefault() is { } turn)
            {
                return turn;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("The turn was not saved.");
    }

    [Fact]
    public async Task Consumers_can_stop_reading_early()
    {
        using var host = new AgentTestHost();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ChatClients.Use("orchestrator", new DelegateChatClient((_, _, token) => DelegateChatClient.TextThenWait("Partial", started, token)));

        var first = await host.WithChatServiceAsync(async chat =>
        {
            await foreach (var streamEvent in chat.StreamAsync(new ChatRequestDto(null, "Question"), Ct))
            {
                return streamEvent;
            }

            return null;
        }).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        first.ShouldBeOfType<ConversationEvent>();
    }

    [Fact]
    public async Task Persistence_failures_are_reported_after_the_answer()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("The answer.");
        host.Conversations.FailAddTurnWith = new InvalidOperationException("database down");

        var events = await host.ChatAsync("Question", cancellationToken: Ct);

        events.Text().ShouldBe("The answer.");
        var error = events.OfType<ErrorEvent>().Single();
        error.Code.ShouldBe("internal_error");
        error.Message.ShouldBe(ChatService.PersistErrorMessage);
        error.Message.ShouldNotContain("database");
        events[^1].ShouldBeOfType<DoneEvent>();
    }

    [Fact]
    public async Task Conversations_can_be_read_and_deleted()
    {
        using var host = new AgentTestHost();
        host.Search.AddHit("Doc", "A passage.");
        host.ChatClients.Script("orchestrator")
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "passage" })
            .ThenText("Answer [S1].");
        host.ChatClients.Script("utility").ThenText("Passage question");

        var events = await host.ChatAsync("Ask about the passage", cancellationToken: Ct);
        var conversationId = events.ConversationId();

        var stored = host.Conversations.All.ShouldHaveSingleItem();
        stored.Id.ShouldBe(conversationId);
        stored.Title.ShouldBe("Passage question");

        var dto = (await host.WithChatServiceAsync(chat => chat.GetConversationAsync(conversationId, Ct))).ShouldNotBeNull();
        dto.Title.ShouldBe("Passage question");
        dto.AgentName.ShouldBe("orchestrator");
        dto.PromptName.ShouldBe(PromptNames.OrchestratorSystem);
        dto.PromptVersion.ShouldBe(3);
        dto.PromptOrigin.ShouldBe("langfuse");
        dto.Profile.ShouldBe("orchestrator");
        dto.SourceIds.ShouldBeEmpty();
        var turn = dto.Turns.ShouldHaveSingleItem();
        turn.Sequence.ShouldBe(1);
        turn.UserMessage.ShouldBe("Ask about the passage");
        turn.AssistantMessage.ShouldBe("Answer [S1].");
        turn.ToolCalls.ShouldHaveSingleItem().ToolName.ShouldBe(ToolNames.SearchKnowledgeBase);
        turn.Citations.ShouldHaveSingleItem().Ref.ShouldBe("S1");
        turn.Model.ShouldBe("orchestrator-model");
        turn.FinishReason.ShouldBe("stop");

        (await host.WithChatServiceAsync(chat => chat.GetConversationAsync(Guid.NewGuid(), Ct))).ShouldBeNull();
        (await host.WithChatServiceAsync(chat => chat.DeleteConversationAsync(conversationId, Ct))).ShouldBeTrue();
        (await host.WithChatServiceAsync(chat => chat.DeleteConversationAsync(conversationId, Ct))).ShouldBeFalse();
        host.Conversations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Title_generation_falls_back_to_the_message_prefix_when_the_model_fails()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("ok");
        host.ChatClients.Script("utility").ThenThrow(new InvalidOperationException("utility model down"));

        var events = await host.ChatAsync("  Where can I find the onboarding checklist for new hires in the Berlin office?  ", cancellationToken: Ct);

        var title = events.OfType<ConversationEvent>().Last().Title;
        title.ShouldBe("Where can I find the onboarding checklist for new hires in t");
        host.Conversations.Get(events.ConversationId()).Title.ShouldBe(title);
    }

    [Fact]
    public async Task Title_generation_uses_the_title_prompt_and_the_utility_profile_without_tools()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("ok");
        var utility = host.ChatClients.Script("utility").ThenText("Title: \"Onboarding checklist location.\"\nIt is about onboarding.");

        var events = await host.ChatAsync("Where is the onboarding checklist?", cancellationToken: Ct);

        events.OfType<ConversationEvent>().Last().Title.ShouldBe("Onboarding checklist location");
        host.Prompts.RendersOf(PromptNames.ConversationTitle).Single().Variables["message"].ShouldBe("Where is the onboarding checklist?");
        var request = utility.Requests.Single();
        request.Messages.Select(m => m.Role).ShouldBe([ChatRole.System, ChatRole.User]);
        request.Messages[0].Text.ShouldBe("Write a short title for: Where is the onboarding checklist?");
        request.Messages[1].Text.ShouldBe("Where is the onboarding checklist?");
        request.Options!.Tools.ShouldBeNull();
        request.Options.ToolMode.ShouldBeNull();
        host.ChatClients.Requests.Single(r => r.Profile == "utility").PromptPatch.ShouldBeNull();
    }

    [Fact]
    public async Task Without_title_generation_the_message_prefix_is_the_title_and_no_second_event_is_sent()
    {
        using var host = new AgentTestHost(new Dictionary<string, string?> { ["Agents:GenerateTitles"] = "false" });
        host.ChatClients.Script("orchestrator").ThenText("ok");
        var utility = host.ChatClients.Script("utility");

        var events = await host.ChatAsync("Summarize the quarterly report", cancellationToken: Ct);

        var conversationEvent = events.OfType<ConversationEvent>().ShouldHaveSingleItem();
        conversationEvent.Title.ShouldBe("Summarize the quarterly report");
        utility.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Second_turns_do_not_generate_titles()
    {
        using var host = new AgentTestHost();
        host.ChatClients.Script("orchestrator").ThenText("one").ThenText("two");
        var utility = host.ChatClients.Script("utility").ThenText("First title");

        var first = await host.ChatAsync("Hi", cancellationToken: Ct);
        var second = await host.ChatAsync("Again", first.ConversationId(), Ct);

        second.OfType<ConversationEvent>().ShouldHaveSingleItem().Title.ShouldBe("First title");
        utility.Requests.Count.ShouldBe(1);
        second.CollapsedTypes().ShouldBe(
        [
            ChatEventTypes.Conversation,
            ChatEventTypes.AgentStarted,
            ChatEventTypes.TextDelta,
            ChatEventTypes.StepFinished,
            ChatEventTypes.Usage,
            ChatEventTypes.AgentCompleted,
            ChatEventTypes.Done,
        ]);
    }
}
