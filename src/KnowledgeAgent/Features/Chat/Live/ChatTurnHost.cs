using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Chat.Live;

public sealed class ConversationBusyException(Guid conversationId)
    : Exception($"The previous answer in conversation {conversationId} is still being written.")
{
    public Guid ConversationId { get; } = conversationId;
}

// One turn per conversation at a time on an instance.
public sealed partial class ChatTurnHost(
    IServiceScopeFactory scopeFactory,
    ConversationEventHub hub,
    IHostApplicationLifetime lifetime,
    ILogger<ChatTurnHost> logger)
{
    private readonly ConcurrentDictionary<Guid, TurnBroadcast> _active = new();

    public bool IsActive(Guid conversationId) => _active.ContainsKey(conversationId);

    public TurnBroadcast? Find(Guid conversationId) => _active.GetValueOrDefault(conversationId);

    public bool Stop(Guid conversationId) => Find(conversationId)?.RequestStop() == true;

    public IAsyncEnumerable<ChatStreamEvent> Start(
        Guid? conversationId,
        Func<IServiceProvider, TurnBroadcast, CancellationToken, Task> run,
        CancellationToken readerCancellation)
    {
        ArgumentNullException.ThrowIfNull(run);
        var broadcast = new TurnBroadcast(this, hub, lifetime.ApplicationStopping);
        if (conversationId is { } id && id != Guid.Empty && !Claim(id, broadcast))
        {
            throw new ConversationBusyException(id);
        }

        _ = Task.Run(() => RunAsync(broadcast, run), CancellationToken.None);
        return broadcast.ReadAllAsync(readerCancellation);
    }

    // A new conversation gets its id inside the turn, so the turn claims it then.
    internal bool Claim(Guid conversationId, TurnBroadcast broadcast)
    {
        if (!_active.TryAdd(conversationId, broadcast))
        {
            return false;
        }

        broadcast.ConversationId = conversationId;
        return true;
    }

    private async Task RunAsync(TurnBroadcast broadcast, Func<IServiceProvider, TurnBroadcast, CancellationToken, Task> run)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await run(scope.ServiceProvider, broadcast, broadcast.Token);
        }
        catch (Exception ex)
        {
            // The executor reports its own failures as events; this catches what escaped it, so the reader is told too.
            LogTurnCrashed(logger, ex, broadcast.ConversationId);
            broadcast.Writer.TryWrite(new ErrorEvent(ChatService.InternalErrorMessage, ChatErrorCodes.InternalError));
        }
        finally
        {
            broadcast.Complete();
        }
    }

    internal void Release(TurnBroadcast broadcast)
    {
        if (broadcast.ConversationId is { } id)
        {
            _active.TryRemove(new KeyValuePair<Guid, TurnBroadcast>(id, broadcast));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A chat turn in conversation {ConversationId} ended with an unhandled error")]
    private static partial void LogTurnCrashed(ILogger logger, Exception exception, Guid? conversationId);
}

public sealed class TurnBroadcast
{
    private readonly ChatTurnHost _host;
    private readonly ConversationEventHub _hub;
    private readonly Lock _gate = new();
    private readonly List<ChatStreamEvent> _events = [];
    private readonly List<ChannelWriter<ChatStreamEvent>> _readers = [];
    private readonly CancellationTokenSource _stop;
    private volatile bool _completed;

    internal TurnBroadcast(ChatTurnHost host, ConversationEventHub hub, CancellationToken applicationStopping)
    {
        _host = host;
        _hub = hub;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        Writer = new BroadcastWriter(this);
    }

    public CancellationToken Token => _stop.Token;

    public bool StopRequested { get; private set; }

    internal bool RequestStop()
    {
        lock (_gate)
        {
            if (_completed || StopRequested)
            {
                return false;
            }

            StopRequested = true;
        }

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The turn ended in between.
        }

        return true;
    }

    public string AnswerSoFar() =>
        string.Concat(Snapshot().OfType<TextDeltaEvent>().Where(item => item.ParentCallId is null).Select(item => item.Text));

    public Guid? ConversationId { get; internal set; }

    // Tells this turn's events apart from the next turn's in live views, where indexes start again at 0.
    public Guid Key { get; } = Guid.NewGuid();

    public ChannelWriter<ChatStreamEvent> Writer { get; }

    public IReadOnlyList<ChatStreamEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }

    public bool TryClaim(Guid conversationId) => ConversationId == conversationId || _host.Claim(conversationId, this);

    public async IAsyncEnumerable<ChatStreamEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ChatStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            foreach (var past in _events)
            {
                channel.Writer.TryWrite(past);
            }

            if (_completed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _readers.Add(channel.Writer);
            }
        }

        try
        {
            await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return streamEvent;
            }
        }
        finally
        {
            lock (_gate)
            {
                _readers.Remove(channel.Writer);
            }
        }
    }

    internal void Complete()
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;

            // The turn has saved everything by now. Letting go of the conversation before the readers see the end
            // means a caller that sends the next message at once is not told this answer is still being written.
            _host.Release(this);
            foreach (var reader in _readers)
            {
                reader.TryComplete();
            }

            _readers.Clear();
        }

        // Linked to the application's stopping token, so it must not outlive the turn.
        _stop.Dispose();
    }

    private bool Add(ChatStreamEvent streamEvent)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            _events.Add(streamEvent);
            foreach (var reader in _readers)
            {
                reader.TryWrite(streamEvent);
            }

            // Inside the lock, so live views get the events in the order of their indexes.
            if (ConversationId is { } conversationId)
            {
                _hub.Publish(conversationId, new TurnStreamEvent(streamEvent, Key, _events.Count - 1));
            }
        }

        return true;
    }

    private sealed class BroadcastWriter(TurnBroadcast broadcast) : ChannelWriter<ChatStreamEvent>
    {
        public override bool TryWrite(ChatStreamEvent item) => broadcast.Add(item);

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(!broadcast._completed);

        public override bool TryComplete([AllowNull] Exception error = null)
        {
            broadcast.Complete();
            return true;
        }
    }
}
