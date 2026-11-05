using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Tests.Features.Sources.Fakes;

// Returns copies, like a database would, so changes are only visible after UpdateAsync.
internal sealed class InMemorySourceRepository : ISourceRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Source> _sources = [];
    private readonly Dictionary<Guid, SourceContent> _contents = [];
    private readonly List<(Guid SourceId, SourceStatus Status)> _statusHistory = [];

    public Exception? GetFailure { get; set; }

    // Runs outside the lock so tests can interleave repository calls.
    public Action<Source>? AfterUpdate { get; set; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sources.Count;
            }
        }
    }

    // Status changes in order; progress updates that keep the status are left out.
    public IReadOnlyList<SourceStatus> StatusHistory(Guid sourceId)
    {
        lock (_gate)
        {
            var history = new List<SourceStatus>();
            foreach (var (id, status) in _statusHistory)
            {
                if (id == sourceId && (history.Count == 0 || history[^1] != status))
                {
                    history.Add(status);
                }
            }

            return history;
        }
    }

    public Source? Find(Guid sourceId)
    {
        lock (_gate)
        {
            return _sources.TryGetValue(sourceId, out var source) ? Clone(source) : null;
        }
    }

    public SourceContent? FindContent(Guid sourceId)
    {
        lock (_gate)
        {
            return _contents.TryGetValue(sourceId, out var content) ? Clone(content) : null;
        }
    }

    public Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_sources.TryAdd(source.Id, Clone(source)))
            {
                throw new InvalidOperationException($"Source {source.Id} already exists.");
            }

            if (content is not null)
            {
                _contents[source.Id] = Clone(content);
                _contents[source.Id].SourceId = source.Id;
            }

            _statusHistory.Add((source.Id, source.Status));
        }

        return Task.FromResult(source);
    }

    public Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (GetFailure is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Find(id));
    }

    public Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Source> sources = [.. _sources.Values.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Select(Clone)];
            return Task.FromResult(sources);
        }
    }

    public Task<IReadOnlyList<Source>> ListByStatusAsync(IReadOnlyCollection<SourceStatus> statuses, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Source> sources = [.. _sources.Values.Where(s => statuses.Contains(s.Status)).Select(Clone)];
            return Task.FromResult(sources);
        }
    }

    public Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default)
    {
        if (GetFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            var page = SourceQueries.Find(_sources.Values, query);
            return Task.FromResult(page with { Items = [.. page.Items.Select(Clone)] });
        }
    }

    public Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(SourceQueries.Stats(_sources.Values));
        }
    }

    public Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(SourceQueries.CountByFolder(_sources.Values));
        }
    }

    public Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
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

            foreach (var page in _sources.Values.Where(source => source.ParentId is { } parentId && ids.Contains(parentId)))
            {
                page.FolderId = folderId;
            }

            return Task.FromResult(moved);
        }
    }

    public Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var matched = SourceQueries.Matching(_sources.Values, query).ToList();
            matched.ForEach(source => source.FolderId = folderId);
            return Task.FromResult(matched.Count);
        }
    }

    public Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(SourceQueries.CountBySite(_sources.Values, query, limit));
        }
    }

    public Task UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sources.TryGetValue(source.Id, out var stored))
            {
                throw new InvalidOperationException($"Source {source.Id} does not exist (deleted concurrently).");
            }

            // Like the EF Core repository, an update keeps the stored folder.
            var updated = Clone(source);
            updated.FolderId = stored.FolderId;
            _sources[source.Id] = updated;
            _statusHistory.Add((source.Id, source.Status));
        }

        AfterUpdate?.Invoke(Clone(source));
        return Task.CompletedTask;
    }

    public Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(FindContent(sourceId));

    public Task SaveContentAsync(Guid sourceId, string? extractedText, byte[]? rawBytes = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_contents.TryGetValue(sourceId, out var content))
            {
                content = new SourceContent { SourceId = sourceId };
                _contents[sourceId] = content;
            }

            content.ExtractedText = extractedText ?? content.ExtractedText;
            content.RawBytes = rawBytes ?? content.RawBytes;
        }

        return Task.CompletedTask;
    }

    public Task UpdateCrawlProgressAsync(Guid websiteId, int found, int done, int failed, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_sources.TryGetValue(websiteId, out var website))
            {
                website.PagesFound = Math.Max(website.PagesFound, found);
                website.PagesDone += done;
                website.PagesFailed += failed;
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _contents.Remove(id);
            return Task.FromResult(_sources.Remove(id));
        }
    }

    private static Source Clone(Source source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Status = source.Status,
        Title = source.Title,
        Url = source.Url,
        Site = source.Site,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
        ContentHash = source.ContentHash,
        ChunkCount = source.ChunkCount,
        PageCount = source.PageCount,
        TokenEstimate = source.TokenEstimate,
        EmbeddingModel = source.EmbeddingModel,
        Error = source.Error,
        Attempts = source.Attempts,
        Stage = source.Stage,
        EmbeddedChunks = source.EmbeddedChunks,
        JobRunId = source.JobRunId,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        ProcessedAt = source.ProcessedAt,
        Metadata = new Dictionary<string, string>(source.Metadata),
        FolderId = source.FolderId,
        ParentId = source.ParentId,
        Crawl = source.Crawl,
        PagesFound = source.PagesFound,
        PagesDone = source.PagesDone,
        PagesFailed = source.PagesFailed,
        LastFetchedAt = source.LastFetchedAt,
    };

    private static SourceContent Clone(SourceContent content) => new()
    {
        SourceId = content.SourceId,
        RawBytes = content.RawBytes?.ToArray(),
        ExtractedText = content.ExtractedText,
    };
}
