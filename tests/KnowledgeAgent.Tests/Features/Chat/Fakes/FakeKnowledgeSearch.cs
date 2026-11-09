using System.Collections.Concurrent;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

public sealed class FakeKnowledgeSearch : IKnowledgeSearch
{
    private readonly ConcurrentQueue<KnowledgeSearchRequest> _requests = new();
    private readonly List<KnowledgeSearchHit> _hits = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<KnowledgeSearchRequest> Requests => _requests.ToList();

    public Func<KnowledgeSearchRequest, CancellationToken, Task<IReadOnlyList<KnowledgeSearchHit>>>? Handler { get; set; }

    // Reported with every result, as if those parts of the search had failed.
    public IReadOnlyList<string> Degraded { get; set; } = [];

    public KnowledgeSearchHit AddHit(
        string sourceTitle,
        string content,
        double score = 0.9,
        int? page = null,
        Guid? sourceId = null,
        Guid? chunkId = null,
        string? url = null,
        int chunkIndex = 0)
    {
        var hit = new KnowledgeSearchHit(chunkId ?? Guid.NewGuid(), sourceId ?? Guid.NewGuid(), chunkIndex, sourceTitle, url, page, content, score);
        lock (_gate)
        {
            _hits.Add(hit);
        }

        return hit;
    }

    public async Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(request);
        if (Handler is { } handler)
        {
            return new KnowledgeSearchResult(await handler(request, cancellationToken), Degraded);
        }

        lock (_gate)
        {
            IReadOnlyList<KnowledgeSearchHit> hits = _hits
                .Where(hit => request.SourceIds is null || request.SourceIds.Contains(hit.SourceId))
                .Take(request.TopK)
                .ToList();
            return new KnowledgeSearchResult(hits, Degraded);
        }
    }
}
