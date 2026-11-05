using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class ConversationEventScenarios(AppFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Conversation_events_replay_stored_messages_resume_after_the_last_event_and_stream_new_turns()
    {
        var app = await fixture.GetAppAsync();
        app.ChatModels.Reset();

        var first = await app.ChatAsync(new ChatRequestDto(null, "Is anything about otters stored?"));
        first.Errors.ShouldBeEmpty(first.Describe());
        var id = first.ConversationId;

        var conversation = await app.Client.GetJsonAsync<ConversationDto>($"/api/conversations/{id}");
        var stored = conversation.Messages.ShouldNotBeNull();
        stored.Select(message => (message.Sequence, message.Author)).ShouldBe([(1, "user"), (2, "assistant")]);
        stored[0].Text.ShouldBe("Is anything about otters stored?");
        stored[1].Text.ShouldBe(first.Text);
        conversation.Turns.ShouldHaveSingleItem().TraceId.ShouldNotBeNullOrWhiteSpace();

        var replayed = await ReadEventsAsync(app, $"/api/conversations/{id}/events?after=0", lastEventId: null, count: 2);
        replayed.Select(e => (e.Event, e.Id)).ShouldBe([("message", "1"), ("message", "2")]);
        replayed[1].Deserialize<ConversationMessageDto>().Text.ShouldBe(first.Text);

        var resumed = await ReadEventsAsync(app, $"/api/conversations/{id}/events", lastEventId: "1", count: 1);
        resumed.ShouldHaveSingleItem().Id.ShouldBe("2");

        using var follow = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{id}/events");
        follow.Headers.Accept.Add(SseReader.AcceptHeader);
        follow.Headers.Add("Last-Event-ID", "2");
        using var response = await app.Client.SendAsync(follow, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var live = new List<SseEvent>();
        var reading = Task.Run(
            async () =>
            {
                await foreach (var sseEvent in SseReader.ReadAsync(response, timeout.Token))
                {
                    live.Add(sseEvent);
                    if (sseEvent.Event == "message" && sseEvent.Id == "4")
                    {
                        return;
                    }
                }
            },
            Ct);

        var second = await app.ChatAsync(new ChatRequestDto(id, "And about beavers?"));
        second.Errors.ShouldBeEmpty(second.Describe());
        await reading.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var messages = live.Where(e => e.Event == "message").ToList();
        messages.Select(e => e.Id).ShouldBe(["3", "4"]);
        messages[0].Deserialize<ConversationMessageDto>().Text.ShouldBe("And about beavers?");
        messages[1].Deserialize<ConversationMessageDto>().Text.ShouldBe(second.Text);
        // Token deltas reach viewers on the instance that runs the turn, which the test host always is.
        var turnTypes = live.Where(e => e.Event == "turn").Select(e => e.Json.GetProperty("type").GetString()).ToList();
        turnTypes.ShouldContain(ChatEventTypes.TextDelta);
        live.FindIndex(e => e.Event == "turn" && e.Json.GetProperty("type").GetString() == ChatEventTypes.TextDelta)
            .ShouldBeGreaterThan(live.FindIndex(e => e.Id == "3"), "the question is stored before the answer is written");

        using var unknownConversation = await app.Client.GetAsync($"/api/conversations/{Guid.NewGuid()}/events", Ct);
        await unknownConversation.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_view_opened_mid_answer_gets_the_answer_so_far_then_the_rest()
    {
        await using var app = await fixture.CreateAppAsync();
        var gated = app.ChatModels.Use("orchestrator", new GatedChatClient("Hello", " world."));

        using var chat = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new ChatRequestDto(null, "Greet me"), options: TestJson.Options) };
        chat.Headers.Accept.Add(SseReader.AcceptHeader);
        using var chatResponse = await app.Client.SendAsync(chat, HttpCompletionOption.ResponseHeadersRead, Ct);
        chatResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        await gated.Started.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        // The client that asked goes away mid-answer, as on a page reload.
        Guid conversationId;
        await using (var chatEvents = SseReader.ReadAsync(chatResponse, Ct).GetAsyncEnumerator(Ct))
        {
            (await chatEvents.MoveNextAsync()).ShouldBeTrue();
            conversationId = chatEvents.Current.Deserialize<ConversationEvent>().ConversationId;
        }

        chatResponse.Dispose();

        using var follow = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/events");
        follow.Headers.Accept.Add(SseReader.AcceptHeader);
        using var response = await app.Client.SendAsync(follow, HttpCompletionOption.ResponseHeadersRead, Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var live = new List<SseEvent>();
        await using (var events = SseReader.ReadAsync(response, timeout.Token).GetAsyncEnumerator(timeout.Token))
        {
            while (await events.MoveNextAsync())
            {
                live.Add(events.Current);
                if (events.Current.Event == "turn" && events.Current.Json.GetProperty("type").GetString() == ChatEventTypes.TextDelta)
                {
                    gated.Release();
                }

                if (events.Current.Event == "message" && events.Current.Id == "2")
                {
                    break;
                }
            }
        }

        live[0].Id.ShouldBe("1", "the stored question comes first");
        var text = string.Concat(live
            .Where(e => e.Event == "turn" && e.Json.GetProperty("type").GetString() == ChatEventTypes.TextDelta)
            .Select(e => e.Json.GetProperty("data").GetProperty("text").GetString()));
        text.ShouldBe("Hello world.", "the answer so far is replayed once, then the rest follows");
        live[^1].Deserialize<ConversationMessageDto>().Text.ShouldBe("Hello world.");
    }

    private static async Task<List<SseEvent>> ReadEventsAsync(KnowledgeAgentApp app, string url, string? lastEventId, int count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(SseReader.AcceptHeader);
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        using var response = await app.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var events = new List<SseEvent>();
        await foreach (var sseEvent in SseReader.ReadAsync(response, timeout.Token))
        {
            events.Add(sseEvent);
            if (events.Count == count)
            {
                break;
            }
        }

        return events;
    }

    private sealed class GatedChatClient(string first, string rest) : IChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Greeting")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, first) { MessageId = "m1" };
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, rest) { MessageId = "m1" };
            yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop, MessageId = "m1" };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

public sealed class InMemoryConversationEventScenarios(InMemoryAppFixture fixture) : ConversationEventScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersConversationEventScenarios(ContainersAppFixture fixture) : ConversationEventScenarios(fixture), IClassFixture<ContainersAppFixture>;
