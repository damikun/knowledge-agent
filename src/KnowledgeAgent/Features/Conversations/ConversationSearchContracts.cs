using System.Buffers.Text;
using System.Globalization;
using System.Text;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.People;

namespace KnowledgeAgent.Features.Conversations;

public enum ConversationSortKey
{
    Updated,
    Created,
}

public sealed record ConversationSearchQuery
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxLabelIds = 20;

    public string? Text { get; init; }
    public ConversationChannel? Channel { get; init; }
    public Guid? WidgetId { get; init; }

    public Guid? ContactId { get; init; }
    public string? CreatedBy { get; init; }

    public IReadOnlyList<KnowledgeAgent.Features.People.ResolvedCondition>? ContactConditions { get; init; }
    public DateTimeOffset Now { get; init; }
    public ConversationStatus? Status { get; init; }

    public IReadOnlyList<ConversationStatus> Statuses { get; init; } = [];
    public bool? HasHandover { get; init; }

    public string? ViewerId { get; init; }

    public string? AssigneeId { get; init; }
    public bool Unassigned { get; init; }

    public bool? Unread { get; init; }

    public bool IncludeTests { get; init; }

    public IReadOnlyList<Guid> LabelIds { get; init; } = [];

    public bool? Pinned { get; init; }

    public Guid? Id { get; init; }

    public int? MinRating { get; init; }
    public int? MaxRating { get; init; }

    public bool ThumbsDown { get; init; }

    // From inclusive, To exclusive.
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }

    public ConversationSortKey Sort { get; init; } = ConversationSortKey.Updated;
    public bool Descending { get; init; } = true;
    public ConversationCursor? After { get; init; }
    public int Limit { get; init; } = DefaultLimit;
}

public sealed record ConversationCursor(DateTimeOffset Value, Guid Id, bool? Pinned = null)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Pinned is { } pinned
            ? string.Create(CultureInfo.InvariantCulture, $"{Value.UtcTicks}:{Id:N}:{(pinned ? 1 : 0)}")
            : string.Create(CultureInfo.InvariantCulture, $"{Value.UtcTicks}:{Id:N}")));

    public static bool TryDecode(string? text, out ConversationCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text.Trim())).Split(':');
            if (parts.Length is 2 or 3
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3_155_378_975_999_999_999
                && Guid.TryParseExact(parts[1], "N", out var id)
                && (parts.Length == 2 || parts[2] is "0" or "1"))
            {
                cursor = new ConversationCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id, parts.Length == 3 ? parts[2] == "1" : null);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

public sealed record ConversationSearchItemDto(
    Guid Id,
    string Title,
    string Channel,
    string Status,
    Guid? AgentId,
    Guid? WidgetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? Preview,
    string? Match,
    string? HandoverStatus,
    int? Rating,
    int ThumbsDown)
{
    public bool Pinned { get; init; }
    public IReadOnlyList<ConversationLabelDto> Labels { get; init; } = [];

    public ConversationContactDto? Contact { get; init; }
    public ConversationCreatorDto? CreatedBy { get; init; }

    public ConversationAssigneeDto? Assignee { get; init; }
    public bool Unread { get; init; }

    public bool Test => Channel == "portal";
}

public sealed record ConversationSearchPageDto(IReadOnlyList<ConversationSearchItemDto> Items, string? NextCursor);

public sealed record ConversationListPageDto(IReadOnlyList<ConversationSummaryDto> Items, string? NextCursor);

public sealed record UpdateConversationRequest(string? Title = null, bool? Pinned = null);

public sealed record ConversationFeedbackItemDto(
    Guid Id,
    Guid? MessageId,
    string Kind,
    int Rating,
    string? Comment,
    IReadOnlyList<string> Reasons,
    string Author,
    DateTimeOffset CreatedAt);

public sealed record ConversationHistoryDto(IReadOnlyList<ConversationFeedbackItemDto> Feedback, IReadOnlyList<HandoverDto> Handovers)
{
    public IReadOnlyList<ConversationLabelDto> Labels { get; init; } = [];
}
