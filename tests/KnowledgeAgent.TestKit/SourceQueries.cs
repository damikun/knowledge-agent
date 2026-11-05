using KnowledgeAgent.Features.Sources;

namespace KnowledgeAgent.TestKit;

public static class SourceQueries
{
    public static SourcePage Find(IEnumerable<Source> sources, SourceQuery query)
    {
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, SourceQuery.MaxLimit);
        var terms = Terms(query);
        var matches = Matching(sources, query).ToList();

        var ordered = terms.Count == 0
            ? matches.OrderByDescending(source => source.CreatedAt).ThenByDescending(source => source.Id)
            : matches
                .OrderByDescending(source => source.Title.StartsWith(string.Join(' ', terms), StringComparison.OrdinalIgnoreCase))
                .ThenBy(source => source.Title, StringComparer.Ordinal)
                .ThenBy(source => source.Id);

        return new SourcePage(ordered.Skip(offset).Take(limit).ToList(), matches.Count, offset);
    }

    public static SourceStats Stats(IEnumerable<Source> sources)
    {
        var list = sources.ToList();
        var ready = list.Where(source => source.Status == SourceStatus.Ready).ToList();
        return new SourceStats(
            list.Where(source => source.ParentId is null).GroupBy(source => source.Status).ToDictionary(group => group.Key, group => group.Count()),
            ready.GroupBy(source => source.Type).ToDictionary(group => group.Key, group => group.Count()),
            ready.Sum(source => (long)source.ChunkCount),
            ready.Where(source => !string.IsNullOrEmpty(source.EmbeddingModel))
                .GroupBy(source => source.EmbeddingModel!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
    }

    public static IEnumerable<Source> Matching(IEnumerable<Source> sources, SourceQuery query)
    {
        var terms = Terms(query);
        return sources
            .Where(source => query.Type is null || source.Type == query.Type)
            .Where(source => query.Statuses is not { Count: > 0 } statuses || statuses.Contains(source.Status))
            .Where(source => query.Ids is not { Count: > 0 } ids || ids.Contains(source.Id))
            .Where(source => query.Folder is not { } folder || source.FolderId == folder.FolderId)
            .Where(source => string.IsNullOrEmpty(query.Site) || source.Site == query.Site)
            .Where(source => query.Parent is not { } parent || source.ParentId == parent.ParentId)
            .Where(source => terms.All(term => Matches(source, term)));
    }

    public static IReadOnlyList<SiteCount> CountBySite(IEnumerable<Source> sources, SourceQuery query, int limit) =>
        [.. Matching(sources, query)
            .Where(source => source.Site is not null)
            .GroupBy(source => source.Site!, StringComparer.Ordinal)
            .Select(group => new SiteCount(group.Key, group.Count()))
            .OrderByDescending(site => site.Count)
            .ThenBy(site => site.Site, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))];

    public static SourceFolderCounts CountByFolder(IEnumerable<Source> sources)
    {
        var list = sources.Where(source => source.ParentId is null).ToList();
        return new SourceFolderCounts(
            list.Where(source => source.FolderId is not null)
                .GroupBy(source => source.FolderId!.Value)
                .ToDictionary(group => group.Key, group => group.Count()),
            list.Count(source => source.FolderId is null),
            list.Count);
    }

    private static List<string> Terms(SourceQuery query) =>
        string.IsNullOrWhiteSpace(query.Text)
            ? []
            : query.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

    private static bool Matches(Source source, string term) =>
        source.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (source.Url?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
        || (source.FileName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
}
