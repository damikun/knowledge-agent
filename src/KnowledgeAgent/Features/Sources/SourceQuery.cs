namespace KnowledgeAgent.Features.Sources;

public sealed record SourceQuery(
    string? Text = null,
    SourceType? Type = null,
    IReadOnlyCollection<SourceStatus>? Statuses = null,
    IReadOnlyCollection<Guid>? Ids = null,
    int Offset = 0,
    int Limit = 20,
    FolderFilter? Folder = null,
    string? Site = null,
    ParentFilter? Parent = null)
{
    public const int MaxLimit = 100;
}

// A null FolderId selects the unfiled sources.
public sealed record FolderFilter(Guid? FolderId)
{
    public static FolderFilter Unfiled { get; } = new((Guid?)null);
}

// A null ParentId selects the sources that are not pages of a website.
public sealed record ParentFilter(Guid? ParentId)
{
    public static ParentFilter TopLevel { get; } = new((Guid?)null);
}

public sealed record SourceFolderCounts(IReadOnlyDictionary<Guid, int> ByFolder, int Unfiled, int Total);

public sealed record SiteCount(string Site, int Count);

public sealed record SourcePage(IReadOnlyList<Source> Items, int Total, int Offset)
{
    public int? NextOffset => Offset + Items.Count < Total ? Offset + Items.Count : null;
}

public sealed record SourceStats(
    IReadOnlyDictionary<SourceStatus, int> CountByStatus,
    IReadOnlyDictionary<SourceType, int> ReadyCountByType,
    long ReadyChunkCount,
    IReadOnlyDictionary<string, int> ReadyCountByEmbeddingModel)
{
    public int Count(SourceStatus status) => CountByStatus.TryGetValue(status, out var count) ? count : 0;
}
