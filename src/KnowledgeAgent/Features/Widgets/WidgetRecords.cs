namespace KnowledgeAgent.Features.Widgets;

public sealed class WidgetAsset
{
    public const int MaxBytes = 512 * 1024;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WidgetId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WidgetVisitor
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WidgetId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }

    public Guid? ContactId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    public int SessionCount { get; set; }
    public string? LastPageUrl { get; set; }
    public string? LastReferrer { get; set; }
    public string? Language { get; set; }
}

public sealed class WidgetDailyUsage
{
    public Guid WidgetId { get; set; }
    public DateOnly Day { get; set; }
    public int Messages { get; set; }
}
