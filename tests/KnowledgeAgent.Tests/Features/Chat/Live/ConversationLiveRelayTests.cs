using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class ConversationLiveRelayTests : IDisposable
{
    private readonly InMemoryConversationRepository _conversations = new();
    private readonly ConversationEventHub _hub = new();
    private readonly FakeLogger<ConversationLiveRelay> _logger = new();
    private readonly ServiceProvider _services;
    private readonly ConversationLiveRelay _relay;
    private int _reads;

    public ConversationLiveRelayTests()
    {
        _services = new ServiceCollection()
            .AddScoped<IConversationRepository>(_ =>
            {
                Interlocked.Increment(ref _reads);
                return FailReadsWith is { } failure ? throw failure : _conversations;
            })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _relay = new ConversationLiveRelay(_services.GetRequiredService<IServiceScopeFactory>(), _hub, _logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Exception? FailReadsWith { get; set; }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Does_not_read_the_database_when_nobody_on_this_instance_follows_the_conversation()
    {
        var conversationId = await CreateAsync(messages: 2);

        await _relay.RelayAsync(new MessagesStored(conversationId, 1), Ct);
        await _relay.RelayAsync(new StatusChanged(conversationId, ConversationStatus.Closed), Ct);

        _reads.ShouldBe(0);
    }

    [Fact]
    public async Task Publishes_every_message_from_the_first_stored_sequence_on()
    {
        var conversationId = await CreateAsync(messages: 7);
        using var view = _hub.Subscribe(conversationId);

        await _relay.RelayAsync(new MessagesStored(conversationId, 6), Ct);

        Drain(view).Select(e => e.ShouldBeOfType<MessageAddedEvent>().Message.Sequence).ShouldBe([6, 7]);
        _reads.ShouldBe(1);
    }

    [Fact]
    public async Task Publishes_a_status_change_without_reading_the_database()
    {
        var conversationId = Guid.NewGuid();
        using var view = _hub.Subscribe(conversationId);

        await _relay.RelayAsync(new StatusChanged(conversationId, ConversationStatus.HandoverPending), Ct);

        var changed = Drain(view).ShouldHaveSingleItem().ShouldBeOfType<ConversationChangedEvent>();
        changed.ShouldBe(new ConversationChangedEvent(conversationId, "handover_pending"));
        _reads.ShouldBe(0);
    }

    [Fact]
    public async Task Logs_and_keeps_going_when_the_database_read_fails()
    {
        var conversationId = await CreateAsync(messages: 1);
        using var view = _hub.Subscribe(conversationId);
        FailReadsWith = new InvalidOperationException("The database is gone.");

        await _relay.RelayAsync(new MessagesStored(conversationId, 1), Ct);

        var warning = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Exception.ShouldBeOfType<InvalidOperationException>();

        FailReadsWith = null;
        await _relay.RelayAsync(new MessagesStored(conversationId, 1), Ct);
        Drain(view).ShouldHaveSingleItem().ShouldBeOfType<MessageAddedEvent>();
    }

    [Fact]
    public async Task Stops_quietly_when_cancelled()
    {
        var conversationId = await CreateAsync(messages: 1);
        using var view = _hub.Subscribe(conversationId);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        FailReadsWith = new OperationCanceledException(cancelled.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => _relay.RelayAsync(new MessagesStored(conversationId, 1), cancelled.Token));

        _logger.Collector.Count.ShouldBe(0, "shutting down is not a failure");
    }

    private async Task<Guid> CreateAsync(int messages)
    {
        var conversation = await _conversations.CreateAsync(new Conversation(), Ct);
        for (var i = 0; i < messages; i++)
        {
            await _conversations.AddMessageAsync(
                new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Author = MessageAuthor.Visitor, Text = $"Message {i + 1}" },
                Ct);
        }

        return conversation.Id;
    }

    private static List<ConversationLiveEvent> Drain(ConversationEventHub.Subscription view)
    {
        var events = new List<ConversationLiveEvent>();
        while (view.Reader.TryRead(out var liveEvent))
        {
            events.Add(liveEvent);
        }

        return events;
    }
}
