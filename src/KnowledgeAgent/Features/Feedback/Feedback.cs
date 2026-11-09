namespace KnowledgeAgent.Features.Feedback;

public enum FeedbackKind
{
    Answer,

    Conversation,

    Agent,
}

public enum FeedbackAuthor
{
    Visitor,
    Staff,

    // A note the assistant wrote from the chat; nobody confirmed it.
    Agent,
}

public sealed class Feedback
{
    public const int MaxCommentLength = 2000;

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ConversationId { get; set; }

    public Guid? MessageId { get; set; }

    public FeedbackKind Kind { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public List<string> Reasons { get; set; } = [];
    public FeedbackAuthor Author { get; set; }

    // Visitor id or staff key, so a second rating replaces the first.
    public string AuthorKey { get; set; } = string.Empty;

    public Guid? WidgetId { get; set; }
    public Guid? AgentId { get; set; }

    public string? TraceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ScoreSentAt { get; set; }
}

public static class FeedbackReasons
{
    public static readonly IReadOnlyList<string> All = ["helpful", "accurate", "clear", "incorrect", "incomplete", "irrelevant", "unclear", "outdated", "other"];
}
