using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Sources;

// Reads return detached entities and writes clear the change tracker, so callers can pass an entity
// back to UpdateAsync later, from this scope or another one.
internal sealed class SourceRepository(KnowledgeDbContext db, DbContextGate gate, TimeProvider timeProvider) : ISourceRepository
{
    public async Task<Source> AddAsync(Source source, SourceContent? content = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            db.Sources.Add(source);
            if (content is not null)
            {
                content.SourceId = source.Id;
                db.SourceContents.Add(content);
            }

            await db.SaveChangesAsync(cancellationToken);
            return source;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking().FirstOrDefaultAsync(source => source.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking()
            .OrderByDescending(source => source.CreatedAt)
            .ThenByDescending(source => source.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, SourceQuery.MaxLimit);
        var terms = LikeSearch.Terms(query.Text);
        var npgsql = db.Database.IsNpgsql();
        var sources = Matching(db.Sources.AsNoTracking(), query);

        using var _ = await gate.EnterAsync(cancellationToken);
        var total = await sources.CountAsync(cancellationToken);
        if (total == 0 || offset >= total)
        {
            return new SourcePage([], total, offset);
        }

        IOrderedQueryable<Source> ordered;
        if (terms.Count == 0)
        {
            ordered = sources.OrderByDescending(source => source.CreatedAt).ThenByDescending(source => source.Id);
        }
        else if (npgsql)
        {
            var prefix = LikeSearch.Escape(string.Join(' ', terms)) + "%";
            ordered = sources
                .OrderByDescending(source => EF.Functions.ILike(source.Title, prefix, LikeSearch.EscapeCharacter))
                .ThenBy(source => source.Title)
                .ThenBy(source => source.Id);
        }
        else
        {
            var prefix = string.Join(' ', terms).ToLowerInvariant();
            ordered = sources
                .OrderByDescending(source => source.Title.ToLowerInvariant().StartsWith(prefix))
                .ThenBy(source => source.Title)
                .ThenBy(source => source.Id);
        }

        var items = await ordered.Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new SourcePage(items, total, offset);
    }

    public async Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var rows = await db.Sources.AsNoTracking()
            .GroupBy(source => new { source.Status, source.Type, source.EmbeddingModel, IsPage = source.ParentId != null })
            .Select(group => new
            {
                group.Key.Status,
                group.Key.Type,
                group.Key.EmbeddingModel,
                group.Key.IsPage,
                Count = group.Count(),
                Chunks = group.Sum(source => (long)source.ChunkCount),
            })
            .ToListAsync(cancellationToken);

        // Statuses count what the library lists, so a website counts once; what is searchable includes its pages.
        var byStatus = rows.Where(row => !row.IsPage).GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Sum(row => row.Count));
        var ready = rows.Where(row => row.Status == SourceStatus.Ready).ToList();
        return new SourceStats(
            byStatus,
            ready.GroupBy(row => row.Type).ToDictionary(group => group.Key, group => group.Sum(row => row.Count)),
            ready.Sum(row => row.Chunks),
            ready.Where(row => !string.IsNullOrEmpty(row.EmbeddingModel))
                .GroupBy(row => row.EmbeddingModel!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Count), StringComparer.Ordinal));
    }

    // Every filter of the query except its paging.
    private IQueryable<Source> Matching(IQueryable<Source> sources, SourceQuery query)
    {
        if (query.Type is { } type)
        {
            sources = sources.Where(source => source.Type == type);
        }

        if (query.Statuses is { Count: > 0 } statuses)
        {
            var wanted = statuses.Distinct().ToList();
            sources = sources.Where(source => wanted.Contains(source.Status));
        }

        if (query.Ids is { Count: > 0 } ids)
        {
            var wanted = ids.Distinct().ToList();
            sources = sources.Where(source => wanted.Contains(source.Id));
        }

        if (query.Folder is { } folder)
        {
            var folderId = folder.FolderId;
            sources = sources.Where(source => source.FolderId == folderId);
        }

        if (query.Parent is { } parent)
        {
            var parentId = parent.ParentId;
            sources = sources.Where(source => source.ParentId == parentId);
        }

        if (!string.IsNullOrEmpty(query.Site))
        {
            var site = query.Site;
            sources = sources.Where(source => source.Site == site);
        }

        var npgsql = db.Database.IsNpgsql();
        foreach (var term in LikeSearch.Terms(query.Text))
        {
            if (npgsql)
            {
                // ILIKE can use the trigram GIN indexes.
                var pattern = "%" + LikeSearch.Escape(term) + "%";
                sources = sources.Where(source =>
                    EF.Functions.ILike(source.Title, pattern, LikeSearch.EscapeCharacter)
                    || (source.Url != null && EF.Functions.ILike(source.Url, pattern, LikeSearch.EscapeCharacter))
                    || (source.FileName != null && EF.Functions.ILike(source.FileName, pattern, LikeSearch.EscapeCharacter)));
            }
            else
            {
                var lowered = term.ToLowerInvariant();
                sources = sources.Where(source =>
                    source.Title.ToLowerInvariant().Contains(lowered)
                    || (source.Url != null && source.Url.ToLowerInvariant().Contains(lowered))
                    || (source.FileName != null && source.FileName.ToLowerInvariant().Contains(lowered)));
            }
        }

        return sources;
    }

    public async Task<IReadOnlyList<Source>> ListByStatusAsync(
        IReadOnlyCollection<SourceStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
        {
            return [];
        }

        var wanted = statuses.Distinct().ToArray();
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking()
            .Where(source => wanted.Contains(source.Status))
            .OrderBy(source => source.CreatedAt)
            .ThenBy(source => source.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListIdsInFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        if (folderIds.Count == 0)
        {
            return [];
        }

        var wanted = folderIds.Distinct().ToArray();
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking()
            .Where(source => source.FolderId != null && wanted.Contains(source.FolderId.Value))
            .Select(source => source.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListIdsOutsideFoldersAsync(IReadOnlyCollection<Guid> folderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        var excluded = folderIds.Distinct().ToArray();
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking()
            .Where(source => source.FolderId == null || !excluded.Contains(source.FolderId.Value))
            .Select(source => source.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var _ = await gate.EnterAsync(cancellationToken);
        source.UpdatedAt = timeProvider.GetUtcNow();

        try
        {
            var entry = db.Entry(source);
            entry.State = EntityState.Modified;

            // Ingestion saves an entity it loaded minutes earlier, which must not undo a move made in the meantime.
            entry.Property(item => item.FolderId).IsModified = false;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new KeyNotFoundException($"Source '{source.Id}' does not exist.", ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // Pages follow their website, so they stay in the folder it is in.
    public async Task<int> MoveToFolderAsync(IReadOnlyCollection<Guid> ids, Guid? folderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return 0;
        }

        var moved = await SetFolderAsync(db.Sources.Where(source => wanted.Contains(source.Id)), folderId, cancellationToken);
        await SetFolderAsync(db.Sources.Where(source => source.ParentId != null && wanted.Contains(source.ParentId.Value)), folderId, cancellationToken);
        return moved;
    }

    public async Task<int> MoveMatchingToFolderAsync(SourceQuery query, Guid? folderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var websites = await WithGateAsync(
            () => Matching(db.Sources.AsNoTracking(), query).Where(source => source.Type == SourceType.Website).Select(source => source.Id).ToListAsync(cancellationToken),
            cancellationToken);
        var moved = await SetFolderAsync(Matching(db.Sources, query), folderId, cancellationToken);
        if (websites.Count > 0)
        {
            await SetFolderAsync(db.Sources.Where(source => source.ParentId != null && websites.Contains(source.ParentId.Value)), folderId, cancellationToken);
        }

        return moved;
    }

    public async Task<IReadOnlyList<Source>> ListPagesAsync(Guid websiteId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking()
            .Where(source => source.ParentId == websiteId)
            .OrderBy(source => source.CreatedAt)
            .ThenBy(source => source.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Source?> FindPageAsync(Guid websiteId, string url, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.Sources.AsNoTracking().FirstOrDefaultAsync(source => source.ParentId == websiteId && source.Url == url, cancellationToken);
    }

    public async Task UpdateCrawlProgressAsync(Guid websiteId, int found, int done, int failed, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsRelational())
        {
            await db.Sources.Where(source => source.Id == websiteId).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.PagesFound, source => source.PagesFound > found ? source.PagesFound : found)
                    .SetProperty(source => source.PagesDone, source => source.PagesDone + done)
                    .SetProperty(source => source.PagesFailed, source => source.PagesFailed + failed),
                cancellationToken);
            return;
        }

        try
        {
            if (await db.Sources.FirstOrDefaultAsync(source => source.Id == websiteId, cancellationToken) is { } website)
            {
                website.PagesFound = Math.Max(website.PagesFound, found);
                website.PagesDone += done;
                website.PagesFailed += failed;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<T> WithGateAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await action();
    }

    public async Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var _ = await gate.EnterAsync(cancellationToken);
        var rows = await Matching(db.Sources.AsNoTracking(), query)
            .Where(source => source.Site != null)
            .GroupBy(source => source.Site!)
            .Select(group => new { Site = group.Key, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Site)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SiteCount(row.Site, row.Count))];
    }

    private async Task<int> SetFolderAsync(IQueryable<Source> sources, Guid? folderId, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        if (db.Database.IsRelational())
        {
            return await sources.ExecuteUpdateAsync(setters => setters.SetProperty(source => source.FolderId, folderId), cancellationToken);
        }

        // Non-relational providers have no ExecuteUpdate.
        try
        {
            var matched = await sources.ToListAsync(cancellationToken);
            foreach (var source in matched)
            {
                source.FolderId = folderId;
            }

            await db.SaveChangesAsync(cancellationToken);
            return matched.Count;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<SourceFolderCounts> CountByFolderAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var rows = await db.Sources.AsNoTracking()
            .Where(source => source.ParentId == null)
            .GroupBy(source => source.FolderId)
            .Select(group => new { FolderId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return new SourceFolderCounts(
            rows.Where(row => row.FolderId is not null).ToDictionary(row => row.FolderId!.Value, row => row.Count),
            rows.Where(row => row.FolderId is null).Sum(row => row.Count),
            rows.Sum(row => row.Count));
    }

    public async Task<SourceContent?> GetContentAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.SourceContents.AsNoTracking().FirstOrDefaultAsync(content => content.SourceId == sourceId, cancellationToken);
    }

    public async Task SaveContentAsync(
        Guid sourceId,
        string? extractedText,
        byte[]? rawBytes = null,
        CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);

        try
        {
            var state = await db.Sources
                .Where(source => source.Id == sourceId)
                .Select(source => new { HasContent = db.SourceContents.Any(content => content.SourceId == source.Id) })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Source '{sourceId}' does not exist.");

            if (!state.HasContent)
            {
                db.SourceContents.Add(new SourceContent { SourceId = sourceId, ExtractedText = extractedText, RawBytes = rawBytes });
            }
            else if (extractedText is null && rawBytes is null)
            {
                return;
            }
            else
            {
                // Writes only the supplied columns without loading the stored payload, which can be large.
                var entry = db.SourceContents.Attach(new SourceContent { SourceId = sourceId, ExtractedText = extractedText, RawBytes = rawBytes });
                entry.Property(content => content.ExtractedText).IsModified = extractedText is not null;
                entry.Property(content => content.RawBytes).IsModified = rawBytes is not null;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);

        if (db.Database.IsRelational())
        {
            // ON DELETE CASCADE removes the content row.
            return await db.Sources.Where(source => source.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        // Non-relational providers have neither ExecuteDelete nor store-side cascades.
        try
        {
            var source = await db.Sources.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (source is null)
            {
                return false;
            }

            var content = await db.SourceContents.FirstOrDefaultAsync(item => item.SourceId == id, cancellationToken);
            if (content is not null)
            {
                db.SourceContents.Remove(content);
            }

            db.Sources.Remove(source);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
