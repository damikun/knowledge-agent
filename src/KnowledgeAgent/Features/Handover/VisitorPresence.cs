using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace KnowledgeAgent.Features.Handover;

// Per instance; replies are emailed only to visitors who left the chat.
public sealed class VisitorPresence
{
    private readonly ConcurrentDictionary<Guid, int> _viewers = new();

    public bool IsPresent(Guid conversationId) => _viewers.TryGetValue(conversationId, out var count) && count > 0;

    public IDisposable Enter(Guid conversationId)
    {
        _viewers.AddOrUpdate(conversationId, 1, (_, count) => count + 1);
        return new Lease(this, conversationId);
    }

    private void Leave(Guid conversationId)
    {
        if (_viewers.AddOrUpdate(conversationId, 0, (_, count) => Math.Max(0, count - 1)) == 0)
        {
            _viewers.TryRemove(new KeyValuePair<Guid, int>(conversationId, 0));
        }
    }

    private sealed class Lease(VisitorPresence presence, Guid conversationId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                presence.Leave(conversationId);
            }
        }
    }
}

internal sealed class PresenceResult(IResult inner, VisitorPresence presence, Guid conversationId) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using var _ = presence.Enter(conversationId);
        await inner.ExecuteAsync(httpContext);
    }
}
