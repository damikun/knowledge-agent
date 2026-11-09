using System.Net;
using System.Threading.Channels;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Scenarios;

// Two API instances on one database, as behind a load balancer.
public sealed class TwoInstanceScenarios
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_view_on_one_instance_sees_messages_and_status_changes_made_on_another()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        var storage = await SharedContainers.CreateIsolatedStorageAsync();
        await using var a = await KnowledgeAgentApp.StartAsync(TestMode.Containers, storage, new KnowledgeAgentAppOptions());
        await using var b = await KnowledgeAgentApp.StartAsync(TestMode.Containers, storage, new KnowledgeAgentAppOptions());

        var first = await a.ChatAsync(new ChatRequestDto(null, "Is anything about otters stored?"));
        first.Errors.ShouldBeEmpty(first.Describe());
        var id = first.ConversationId;

        using var follow = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{id}/events");
        follow.Headers.Accept.Add(SseReader.AcceptHeader);
        follow.Headers.Add("Last-Event-ID", "2");
        using var response = await b.Client.SendAsync(follow, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var received = Channel.CreateUnbounded<SseEvent>();
        var reading = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var sseEvent in SseReader.ReadAsync(response, stop.Token))
                    {
                        received.Writer.TryWrite(sseEvent);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                finally
                {
                    received.Writer.TryComplete();
                }
            },
            Ct);

        // B starts listening in the background, so A repeats an update until one gets through.
        SseEvent? probe = null;
        while (probe is null)
        {
            await SetStatusAsync(a, id, ConversationStatus.Ai);
            probe = await NextAsync(received.Reader, TimeSpan.FromSeconds(1));
        }

        probe.Event.ShouldBe("conversation");

        var second = await a.ChatAsync(new ChatRequestDto(id, "And about beavers?"));
        second.Errors.ShouldBeEmpty(second.Describe());
        await SetStatusAsync(a, id, ConversationStatus.Closed);

        var live = new List<SseEvent>();
        while (live.LastOrDefault() is not { Event: "conversation" } last || last.Json.GetProperty("status").GetString() != "closed")
        {
            live.Add((await NextAsync(received.Reader, TimeSpan.FromSeconds(30))).ShouldNotBeNull("B relays what A stored"));
        }

        var messages = live.Where(e => e.Event == "message").ToList();
        messages.Select(e => e.Id).ShouldBe(["3", "4"]);
        messages[0].Deserialize<ConversationMessageDto>().Text.ShouldBe("And about beavers?");
        messages[1].Deserialize<ConversationMessageDto>().Text.ShouldBe(second.Text);
        live[^1].Json.GetProperty("conversationId").GetGuid().ShouldBe(id);
        live.ShouldNotContain(e => e.Event == "turn", "token deltas stay on the instance that runs the turn");

        await stop.CancelAsync();
        await reading;
    }

    private static async Task SetStatusAsync(KnowledgeAgentApp app, Guid conversationId, ConversationStatus status)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ConversationTimeline>().SetStatusAsync(conversationId, status, Ct);
    }

    // Null when nothing arrived in time.
    private static async Task<SseEvent?> NextAsync(ChannelReader<SseEvent> events, TimeSpan within)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(within);
        try
        {
            return await events.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
