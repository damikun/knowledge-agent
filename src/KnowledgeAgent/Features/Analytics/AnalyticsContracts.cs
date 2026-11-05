namespace KnowledgeAgent.Features.Analytics;

public sealed record AnalyticsDto(
    int Days,
    DateTimeOffset From,
    DateTimeOffset To,
    AnalyticsTotalsDto Totals,
    IReadOnlyList<AnalyticsDayDto> Daily,
    IReadOnlyList<AnalyticsWidgetDto> Widgets,
    IReadOnlyList<UnansweredQuestionDto> Unanswered)
{
    public IReadOnlyList<AnalyticsLabelDto> Labels { get; init; } = [];

    public IReadOnlyList<AnalyticsSegmentDto> Segments { get; init; } = [];
}

public sealed record AnalyticsSegmentDto(
    Guid SegmentId,
    string Name,
    string Color,
    int Contacts,
    int Conversations,
    int HandedToPerson,
    double? HandoverRate,
    int Ratings,
    double? AverageRating);

public sealed record AnalyticsLabelDto(Guid LabelId, string Name, string Color, int Conversations, int PreviousConversations);

public sealed record AnalyticsTotalsDto(
    int Conversations,
    int AnsweredWithoutPerson,
    int HandedToPerson,
    int Ratings,
    double? AverageRating,
    int ThumbsUp,
    int ThumbsDown,
    int NoSourceAnswers);

public sealed record AnalyticsDayDto(
    DateOnly Date,
    int Conversations,
    int HandedToPerson,
    int ThumbsUp,
    int ThumbsDown,
    int Ratings,
    double? AverageRating);

public sealed record AnalyticsWidgetDto(
    Guid? WidgetId,
    int Conversations,
    int HandedToPerson,
    int ThumbsUp,
    int ThumbsDown,
    int Ratings,
    double? AverageRating);

public static class UnansweredReasons
{
    public const string Handover = "handover";
    public const string ThumbsDown = "thumbs_down";
    public const string NoSources = "no_sources";
}

public sealed record UnansweredQuestionDto(
    string Question,
    int Count,
    IReadOnlyList<string> Reasons,
    DateTimeOffset LastAskedAt,
    Guid ConversationId,
    Guid? WidgetId,
    Guid? MessageId,
    string? Answer,
    string? Comment);
