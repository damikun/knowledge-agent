namespace KnowledgeAgent.Features.Sources.Folders;

// Folders organize the Sources page, and agents can be limited to some of them. The Internal folder is the one exception
// with a meaning of its own: what is filed in it or its subfolders only the built-in agent searches.
public sealed class SourceFolder
{
    public const int MaxNameLength = 100;

    // Top-level folders count as level 1.
    public const int MaxDepth = 3;

    // The Internal folder, created by the AddInternalSourceFolder migration. It cannot be renamed, moved or deleted.
    public static readonly Guid InternalId = new("0193a5c4-0000-7000-8000-00000000f01d");
    public const string InternalName = "Internal";

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }

    // Set only on the Internal folder; its subfolders are internal because they are inside it.
    public bool IsInternal { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
