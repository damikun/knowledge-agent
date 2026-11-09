using System.Collections.Concurrent;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class ConversationTimelineTests
{
    private readonly InMemoryConversationRepository _conversations = new();
    private readonly ConversationEventHub _hub = new();
    private readonly RecordingBus _bus = new();
    private readonly FakeLogger<ConversationTimeline> _logger = new();
    private readonly ConversationTimeline _timeline;

    public ConversationTimelineTests() => _timeline = new ConversationTimeline(_conversations, _hub, _bus, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stores_publishes_locally_then_tells_the_other_instances()
    {
        var conversationId = await CreateAsync();
        using var view = _hub.Subscribe(conversationId);

        await AddAsync(conversationId, "First");
        var saved = await AddAsync(conversationId, "Second");

        saved.Sequence.ShouldBe(2);
        view.Reader.TryRead(out var first).ShouldBeTrue();
        first.ShouldBeOfType<MessageAddedEvent>().Message.Sequence.ShouldBe(1);
        _bus.Published.ShouldBe([new MessagesStored(conversationId, 1), new MessagesStored(conversationId, 2)]);
    }

    [Fact]
    public async Task A_failing_bus_does_not_fail_the_write()
    {
        var conversationId = await CreateAsync();
        _bus.FailWith = new TimeoutException("No answer from the database.");

        var saved = await AddAsync(conversationId, "Still stored");
        await _timeline.SetStatusAsync(conversationId, ConversationStatus.Human, Ct);

        (await _conversations.ListMessagesAsync(conversationId, cancellationToken: Ct)).ShouldHaveSingleItem().Id.ShouldBe(saved.Id);
        _conversations.Get(conversationId).Status.ShouldBe(ConversationStatus.Human);
        var warnings = _logger.Collector.GetSnapshot();
        warnings.Count.ShouldBe(2);
        warnings.ShouldAllBe(record => record.Level == LogLevel.Warning && record.Exception is TimeoutException);
    }

    [Fact]
    public async Task A_bus_that_gives_up_does_not_fail_the_write_either()
    {
        var conversationId = await CreateAsync();
        _bus.FailWith = new OperationCanceledException();

        await AddAsync(conversationId, "Still stored");

        (await _conversations.ListMessagesAsync(conversationId, cancellationToken: Ct)).ShouldHaveSingleItem();
        _logger.Collector.LatestRecord.Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task Status_changes_reach_the_bus_only_when_they_happened()
    {
        var conversationId = await CreateAsync();
        using var view = _hub.Subscribe(conversationId);

        await _timeline.SetStatusAsync(conversationId, ConversationStatus.HandoverPending, Ct);
        (await _timeline.TrySetStatusAsync(conversationId, ConversationStatus.Ai, ConversationStatus.Closed, cancellationToken: Ct)).ShouldBeFalse();
        (await _timeline.TrySetStatusAsync(conversationId, ConversationStatus.HandoverPending, ConversationStatus.Human, cancellationToken: Ct)).ShouldBeTrue();

        _bus.Published.ShouldBe(
            [new StatusChanged(conversationId, ConversationStatus.HandoverPending), new StatusChanged(conversationId, ConversationStatus.Human)]);
        view.Reader.TryRead(out var first).ShouldBeTrue();
        first.ShouldBe(new ConversationChangedEvent(conversationId, "handover_pending"));
        view.Reader.TryRead(out var second).ShouldBeTrue();
        second.ShouldBe(new ConversationChangedEvent(conversationId, "human"));
    }

    private async Task<Guid> CreateAsync() => (await _conversations.CreateAsync(new Conversation(), Ct)).Id;

    private Task<ConversationMessage> AddAsync(Guid conversationId, string text) =>
        _timeline.AddAsync(new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversationId, Author = MessageAuthor.Visitor, Text = text }, Ct);

    private sealed class RecordingBus : ILiveBus
    {
        private readonly ConcurrentQueue<LiveMessage> _published = new();

        public Exception? FailWith { get; set; }

        public IReadOnlyList<LiveMessage> Published => [.. _published];

        public Task PublishAsync(LiveMessage message, CancellationToken cancellationToken = default)
        {
            if (FailWith is { } failure)
            {
                throw failure;
            }

            _published.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
