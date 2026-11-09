using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class LivePayloadTests
{
    private readonly LivePayload _payloads = new();

    [Fact]
    public void Round_trips_a_stored_message_and_a_status_change()
    {
        var conversationId = Guid.NewGuid();
        LiveMessage[] messages =
        [
            new MessagesStored(conversationId, 7),
            new StatusChanged(conversationId, ConversationStatus.HandoverPending),
            new StatusChanged(conversationId, ConversationStatus.Closed),
        ];

        foreach (var message in messages)
        {
            LivePayload.TryParse(_payloads.Format(message), out var parsed, out _).ShouldBeTrue();
            parsed.ShouldBe(message);
        }
    }

    [Fact]
    public void Names_the_kind_and_stays_small()
    {
        var conversationId = Guid.NewGuid();

        var stored = _payloads.Format(new MessagesStored(conversationId, 12));
        var status = _payloads.Format(new StatusChanged(conversationId, ConversationStatus.HandoverPending));

        stored.ShouldBe($"message:{conversationId:N}:{_payloads.InstanceId:N}:12");
        status.ShouldBe($"status:{conversationId:N}:{_payloads.InstanceId:N}:handover_pending");
        status.Length.ShouldBeLessThan(LivePayload.MaxBytes);
    }

    [Fact]
    public void Carries_the_instance_that_published_it()
    {
        var other = new LivePayload();

        LivePayload.TryParse(other.Format(new MessagesStored(Guid.NewGuid(), 1)), out _, out var origin).ShouldBeTrue();

        origin.ShouldBe(other.InstanceId);
        origin.ShouldNotBe(_payloads.InstanceId);
    }

    [Fact]
    public void Rejects_the_old_three_part_payload()
    {
        LivePayload.TryParse($"{Guid.NewGuid():N}:3:{Guid.NewGuid():N}", out var message, out var origin).ShouldBeFalse();

        message.ShouldBeNull();
        origin.ShouldBe(Guid.Empty);
    }

    [Theory]
    [InlineData("typing", "1")]
    [InlineData("status", "archived")]
    [InlineData("status", "")]
    [InlineData("message", "0")]
    [InlineData("message", "-2")]
    [InlineData("message", "two")]
    [InlineData("message", "3:4")]
    public void Rejects_an_unknown_kind_or_value(string kind, string value)
    {
        LivePayload.TryParse($"{kind}:{Guid.NewGuid():N}:{Guid.NewGuid():N}:{value}", out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("message:not-a-guid:also-not:1")]
    public void Rejects_what_is_not_a_payload(string? payload)
    {
        LivePayload.TryParse(payload, out _, out _).ShouldBeFalse();
    }
}
