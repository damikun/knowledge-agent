using System.Collections.Concurrent;
using System.Threading.Channels;

namespace KnowledgeAgent.Features.Chat.Live;

public abstract record ConversationLiveEvent;

public sealed record MessageAddedEvent(ConversationMessageDto Message) : ConversationLiveEvent;

// Index is the event's position in its turn, so a view that replayed the turn so far can skip what it already sent.
public sealed record TurnStreamEvent(ChatStreamEvent Event, Guid TurnKey, int Index) : ConversationLiveEvent;

public sealed record ConversationChangedEvent(Guid ConversationId, string Status) : ConversationLiveEvent;

public sealed class ConversationEventHub
{
    // A view that cannot keep up loses the oldest events; it catches up from the stored messages when it reconnects.
    private const int SubscriberCapacity = 1_000;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<ConversationLiveEvent>>> _subscribers = new();

    // Guards subscribe and unsubscribe, so a join racing the last leave isn't added to a set being removed.
    // Publishing reads without it.
    private readonly Lock _membership = new();

    public Subscription Subscribe(Guid conversationId)
    {
        var channel = Channel.CreateBounded<ConversationLiveEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Guid.NewGuid();
        lock (_membership)
        {
            _subscribers.GetOrAdd(conversationId, _ => new()).TryAdd(id, channel.Writer);
        }

        return new Subscription(channel.Reader, () => Unsubscribe(conversationId, id, channel.Writer));
    }

    public void Publish(Guid conversationId, ConversationLiveEvent liveEvent)
    {
        if (!_subscribers.TryGetValue(conversationId, out var subscribers))
        {
            return;
        }

        foreach (var writer in subscribers.Values)
        {
            writer.TryWrite(liveEvent);
        }
    }

    public bool HasSubscribers(Guid conversationId) =>
        _subscribers.TryGetValue(conversationId, out var subscribers) && !subscribers.IsEmpty;

    private void Unsubscribe(Guid conversationId, Guid id, ChannelWriter<ConversationLiveEvent> writer)
    {
        writer.TryComplete();
        lock (_membership)
        {
            if (_subscribers.TryGetValue(conversationId, out var subscribers))
            {
                subscribers.TryRemove(id, out _);
                if (subscribers.IsEmpty)
                {
                    _subscribers.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, ChannelWriter<ConversationLiveEvent>>>(conversationId, subscribers));
                }
            }
        }
    }

    public sealed class Subscription(ChannelReader<ConversationLiveEvent> reader, Action unsubscribe) : IDisposable
    {
        public ChannelReader<ConversationLiveEvent> Reader { get; } = reader;

        public void Dispose() => unsubscribe();
    }
}
