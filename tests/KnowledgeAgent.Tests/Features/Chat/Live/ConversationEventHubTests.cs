using KnowledgeAgent.Features.Chat.Live;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class ConversationEventHubTests
{
    [Fact]
    public async Task A_view_that_subscribes_while_the_last_other_one_leaves_still_gets_the_events()
    {
        var hub = new ConversationEventHub();
        for (var round = 0; round < 2_000; round++)
        {
            var conversationId = Guid.NewGuid();
            var leaving = hub.Subscribe(conversationId);
            ConversationEventHub.Subscription? joining = null;
            using var start = new Barrier(2);

            await Task.WhenAll(
                Task.Run(() =>
                {
                    start.SignalAndWait();
                    leaving.Dispose();
                }),
                Task.Run(() =>
                {
                    start.SignalAndWait();
                    joining = hub.Subscribe(conversationId);
                }));

            using (joining)
            {
                hub.Publish(conversationId, new ConversationChangedEvent(conversationId, "open"));
                joining!.Reader.TryRead(out var received).ShouldBeTrue($"round {round}");
                received.ShouldBeOfType<ConversationChangedEvent>();
            }

            hub.HasSubscribers(conversationId).ShouldBeFalse();
        }
    }
}
