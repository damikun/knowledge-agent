namespace KnowledgeAgent.Features.Overview;

public sealed record OverviewDto(
    int Days,
    DateTimeOffset From,
    int Conversations,
    int WebsiteConversations,

    int PreviousConversations,

    // Nobody asked for a person, or the visitor went back to the assistant before anyone joined.
    int AnsweredByAi,
    int HandedToPerson,

    double? AverageRating,
    int Ratings,
    IReadOnlyList<WidgetActivityDto> Widgets);

public sealed record WidgetActivityDto(Guid WidgetId, int Conversations, DateTimeOffset? LastConversationAt);
