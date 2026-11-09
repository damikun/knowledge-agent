using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Tests.Features.Chat.Fakes;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace KnowledgeAgent.Tests.Features.Chat.Live;

public sealed class PostgresLiveBusTests(PostgresPersistenceFixture fixture) : IClassFixture<PostgresPersistenceFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_instance_relays_a_stored_message_to_its_views()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var conversations = new InMemoryConversationRepository();
        var conversationId = await CreateAsync(conversations, messages: 3);
        await using var a = new Instance(dataSource, conversations);
        await using var b = new Instance(dataSource, conversations);
        await b.StartAsync();
        using var view = b.Hub.Subscribe(conversationId);

        await a.Bus.PublishAsync(new MessagesStored(conversationId, 2), Ct);

        (await ReadAsync(view, 2)).Select(e => e.ShouldBeOfType<MessageAddedEvent>().Message.Sequence).ShouldBe([2, 3]);
    }

    [Fact]
    public async Task A_status_change_reaches_the_other_instance_and_the_publisher_ignores_its_own()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var conversations = new InMemoryConversationRepository();
        var conversationId = await CreateAsync(conversations, messages: 1);
        await using var a = new Instance(dataSource, conversations);
        await using var b = new Instance(dataSource, conversations);
        await Task.WhenAll(a.StartAsync(), b.StartAsync());
        using var viewOnA = a.Hub.Subscribe(conversationId);
        using var viewOnB = b.Hub.Subscribe(conversationId);

        await a.Bus.PublishAsync(new MessagesStored(conversationId, 1), Ct);
        await b.Bus.PublishAsync(new StatusChanged(conversationId, ConversationStatus.Human), Ct);

        // A relays in arrival order, so by the time B's status shows up its own message has been skipped.
        (await ReadAsync(viewOnA, 1)).ShouldHaveSingleItem().ShouldBe(new ConversationChangedEvent(conversationId, "human"));
        a.Reads.ShouldBe(0);
        (await ReadAsync(viewOnB, 1)).ShouldHaveSingleItem().ShouldBeOfType<MessageAddedEvent>();
        b.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Notifications_are_relayed_in_the_order_they_were_sent()
    {
        const int count = 20;
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var conversations = new InMemoryConversationRepository();
        var conversationId = await CreateAsync(conversations, messages: count);
        await using var a = new Instance(dataSource, conversations);
        await using var b = new Instance(dataSource, conversations);
        await b.StartAsync();
        using var view = b.Hub.Subscribe(conversationId);

        for (var first = 1; first <= count; first++)
        {
            await a.Bus.PublishAsync(new MessagesStored(conversationId, first), Ct);
        }

        var expected = Enumerable.Range(1, count).SelectMany(first => Enumerable.Range(first, count - first + 1)).ToList();
        var received = await ReadAsync(view, expected.Count);
        received.Select(e => ((MessageAddedEvent)e).Message.Sequence).ShouldBe(expected);
    }

    [Fact]
    public async Task Nobody_following_the_conversation_means_no_database_read()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var conversations = new InMemoryConversationRepository();
        var unfollowed = await CreateAsync(conversations, messages: 1);
        var followed = Guid.NewGuid();
        await using var a = new Instance(dataSource, conversations);
        await using var b = new Instance(dataSource, conversations);
        await b.StartAsync();
        using var view = b.Hub.Subscribe(followed);

        await a.Bus.PublishAsync(new MessagesStored(unfollowed, 1), Ct);
        await a.Bus.PublishAsync(new StatusChanged(followed, ConversationStatus.Closed), Ct);

        (await ReadAsync(view, 1)).ShouldHaveSingleItem().ShouldBeOfType<ConversationChangedEvent>();
        b.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Stopping_the_listener_is_prompt_and_gives_its_connection_back()
    {
        // One connection in the pool: it can be opened again only if the listener gave it back.
        var single = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { MaxPoolSize = 1, Timeout = 5 };
        await using var dataSource = NpgsqlDataSource.Create(single.ConnectionString);
        await using var b = new Instance(dataSource, new InMemoryConversationRepository());
        await b.StartAsync();

        await b.Listener.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        b.Listener.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
        await using var connection = await dataSource.OpenConnectionAsync(Ct);
        await using var listening = new NpgsqlCommand("SELECT count(*) FROM pg_listening_channels()", connection);
        (await listening.ExecuteScalarAsync(Ct)).ShouldBe(0L, "the pool resets a returned connection");
    }

    [Fact]
    public async Task Publishing_after_the_data_source_is_disposed_is_logged_not_thrown()
    {
        var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var logger = new FakeLogger<PostgresLiveBus>();
        var bus = new PostgresLiveBus(dataSource, new LivePayload(), logger);
        await dataSource.DisposeAsync();

        await bus.PublishAsync(new MessagesStored(Guid.NewGuid(), 1), Ct);

        logger.Collector.LatestRecord.Level.ShouldBe(LogLevel.Warning);
        logger.Collector.LatestRecord.Exception.ShouldBeAssignableTo<InvalidOperationException>();
    }

    private static async Task<Guid> CreateAsync(InMemoryConversationRepository conversations, int messages)
    {
        var conversation = await conversations.CreateAsync(new Conversation(), Ct);
        for (var i = 0; i < messages; i++)
        {
            await conversations.AddMessageAsync(
                new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Author = MessageAuthor.Visitor, Text = $"Message {i + 1}" },
                Ct);
        }

        return conversation.Id;
    }

    private static async Task<List<ConversationLiveEvent>> ReadAsync(ConversationEventHub.Subscription view, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Patience);
        var events = new List<ConversationLiveEvent>();
        while (events.Count < count)
        {
            events.Add(await view.Reader.ReadAsync(timeout.Token));
        }

        return events;
    }

    // One app instance's share of the bus: its own hub, payload identity, relay and listener over a shared database.
    private sealed class Instance : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly FakeLogger<PostgresLiveListener> _listenerLogger = new();
        private int _reads;

        public Instance(NpgsqlDataSource dataSource, IConversationRepository conversations)
        {
            _services = new ServiceCollection()
                .AddScoped(_ =>
                {
                    Interlocked.Increment(ref _reads);
                    return conversations;
                })
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var payloads = new LivePayload();
            var relay = new ConversationLiveRelay(_services.GetRequiredService<IServiceScopeFactory>(), Hub, NullLogger<ConversationLiveRelay>.Instance);
            Bus = new PostgresLiveBus(dataSource, payloads, NullLogger<PostgresLiveBus>.Instance);
            Listener = new PostgresLiveListener(dataSource, payloads, relay, _listenerLogger);
        }

        public ConversationEventHub Hub { get; } = new();
        public PostgresLiveBus Bus { get; }
        public PostgresLiveListener Listener { get; }
        public int Reads => Volatile.Read(ref _reads);

        // Returns once LISTEN has run, so nothing published afterwards is missed.
        public async Task StartAsync()
        {
            await Listener.StartAsync(Ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(Patience);
            while (!_listenerLogger.Collector.GetSnapshot().Any(record => record.Message.StartsWith("Listening for live updates", StringComparison.Ordinal)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Listener.StopAsync(CancellationToken.None);
            Listener.Dispose();
            await _services.DisposeAsync();
        }
    }
}
