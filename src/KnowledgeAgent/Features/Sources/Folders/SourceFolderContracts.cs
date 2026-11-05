namespace KnowledgeAgent.Features.Sources.Folders;

public sealed record SourceFolderDto(
    Guid Id,
    string Name,
    Guid? ParentId,
    int SourceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    // The Internal folder: only the built-in agent searches what is in it and its subfolders. Never renamed, moved or deleted.
    public bool IsInternal { get; init; }
}

public sealed record SourceFolderListDto(IReadOnlyList<SourceFolderDto> Folders, int UnfiledCount, int TotalCount);

public sealed record CreateSourceFolderRequest(string? Name, Guid? ParentId = null);

public sealed record RenameSourceFolderRequest(string? Name);

public sealed record MoveSourceFolderRequest(Guid? ParentId);

// Either sourceIds, or matching with the filters of GET /api/sources to move every source they match.
public sealed record MoveSourcesRequest(IReadOnlyList<Guid>? SourceIds, Guid? FolderId, SourceMatchRequest? Matching = null);

public sealed record SourceMatchRequest(string? Query = null, string? Type = null, string? Status = null, string? Folder = null, string? Site = null);

public sealed record MoveSourcesResponse(int Moved);
