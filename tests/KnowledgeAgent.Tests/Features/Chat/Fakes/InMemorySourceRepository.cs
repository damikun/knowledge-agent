using System.Collections.Concurrent;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

public sealed class InMemorySourceRepository : ISourceRepository
{
    private readonly ConcurrentDictionary<Guid, Source> _sources = new();
    private readonly ConcurrentDictionary<Guid, SourceContent> _contents = new();

    public Source Add(
        string title,
        SourceStatus status = SourceStatus.Ready,
        SourceType type = SourceType.Text,
        string? text = null,
        string? url = null,
        int chunkCount = 3,
        DateTimeOffset? createdAt = null,
        Guid? id = null)
    {
        var source = new Source
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            Status = status,
            Type = type,
            Url = url,
            ChunkCount = chunkCount,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
        _sources[source.Id] = source;
        if (text is not null)
        {
            _contents[source.Id] = new SourceContent { SourceId = source.Id, ExtractedText = text };
        }

        return source;
    }

    public Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default)
    {
        _sources[source.Id] = source;
        if (content is not null)
        {
            _contents[source.Id] = content;
        }

        return Task.FromResult(source);
    }

    public Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sources.GetValueOrDefault(id));

    public Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Source>>(_sources.Values.OrderByDescending(source => source.CreatedAt).ToList());

    public Task<IReadOnlyList<Source>> ListByStatusAsync(IReadOnlyCollection<SourceStatus> statuses, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Source>>(_sources.Values
            .Where(source => statuses.Contains(source.Status))
            .OrderByDescending(source => source.CreatedAt)
            .ToList());

    public Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(SourceQueries.Find(_sources.Values, query));

    public Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SourceQueries.Stats(_sources.Values));

    public Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SourceQueries.CountByFolder(_sources.Values));

    public Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var moved = 0;
        foreach (var id in ids.Distinct())
        {
            if (_sources.TryGetValue(id, out var source))
            {
                source.FolderId = folderId;
                moved++;
            }
        }

        return Task.FromResult(moved);
    }

    public Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var matched = SourceQueries.Matching(_sources.Values, query).ToList();
        matched.ForEach(source => source.FolderId = folderId);
        return Task.FromResult(matched.Count);
    }

    public Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult(SourceQueries.CountBySite(_sources.Values, query, limit));

    public Task UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        _sources[source.Id] = source;
        return Task.CompletedTask;
    }

    public Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_contents.GetValueOrDefault(sourceId));

    public Task SaveContentAsync(Guid sourceId, string? extractedText, byte[]? rawBytes = null, CancellationToken cancellationToken = default)
    {
        var content = _contents.GetOrAdd(sourceId, id => new SourceContent { SourceId = id });
        content.ExtractedText = extractedText ?? content.ExtractedText;
        content.RawBytes = rawBytes ?? content.RawBytes;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _contents.TryRemove(id, out _);
        return Task.FromResult(_sources.TryRemove(id, out _));
    }
}
