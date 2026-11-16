namespace KnowledgeAgent.Features.Sources.Folders;

public sealed class SourceFolder
{
    public const int MaxNameLength = 100;

    public const int MaxDepth = 3;

    public static readonly Guid InternalId = new("0193a5c4-0000-7000-8000-00000000f01d");
    public const string InternalName = "Internal";

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }

    public bool IsInternal { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
