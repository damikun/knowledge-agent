using System.Text.Json;
using KnowledgeAgent.Features.Conversations;

namespace KnowledgeAgent.Features.People;

public sealed record PeopleQuery
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public string? Text { get; init; }
    public Guid? WidgetId { get; init; }
    public bool? Identified { get; init; }

    public bool ActiveOnly { get; init; }

    public IReadOnlyList<ResolvedCondition> Conditions { get; init; } = [];
    public DateTimeOffset Now { get; init; }
    public ConversationCursor? After { get; init; }
    public int Limit { get; init; } = DefaultLimit;
}

public sealed record PersonWidgetDto(Guid Id, string Name);

public sealed record PersonSummaryDto(
    Guid Id,
    string Name,
    string? Email,
    string? ExternalId,
    bool Identified,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int ConversationCount,
    IReadOnlyList<PersonWidgetDto> Widgets)
{
    public int SessionCount { get; init; }
    public bool Returning { get; init; }
    public string? Language { get; init; }
    public string? LastPageUrl { get; init; }

    public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } = new Dictionary<string, JsonElement>();
}

public sealed record PeoplePageDto(IReadOnlyList<PersonSummaryDto> Items, string? NextCursor);

public sealed record PeopleCountDto(int Count);

public sealed record PersonAttributeDto(string Key, string Label, string Type, JsonElement Value, string Source, DateTimeOffset UpdatedAt, bool Visible);

public sealed record UpdatePersonAttributesRequest(Dictionary<string, JsonElement>? Attributes = null);

public sealed record AttributeChangeDto(IReadOnlyList<string> Applied, IReadOnlyList<AttributeRejection> Rejected);

public sealed record ContactAttributeDto(
    string Key,
    string Label,
    string Type,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int Contacts,
    bool Visible,
    bool ExposeToAgent,
    string Trust);

public sealed record ContactAttributeListDto(IReadOnlyList<ContactAttributeDto> Items, IReadOnlyList<ContactFilterAttributeDto> BuiltIn, IReadOnlyDictionary<string, IReadOnlyList<string>> Ops);

public sealed record UpdateContactAttributeRequest(string? Label = null, bool? Visible = null, bool? ExposeToAgent = null, string? Trust = null);

public sealed record DeleteContactAttributeRequest(bool Confirm = false);

public sealed record DeleteContactAttributeResultDto(int Contacts);

public sealed record AttributeValueCountDto(string Value, int Contacts);

public sealed record PersonWidgetUsageDto(Guid Id, string Name, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

public sealed record PersonStatsDto(int Conversations, int Handovers, double? AverageRating, int Ratings, DateTimeOffset LastSeenAt);

public sealed record PersonDto(
    Guid Id,
    string Name,
    string? SavedName,
    string? Email,
    string? ExternalId,
    bool Identified,
    string? Notes,
    IReadOnlyList<PersonAttributeDto> Attributes,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<PersonWidgetUsageDto> Widgets,
    PersonStatsDto Stats)
{
    public int SessionCount { get; init; }
    public bool Returning { get; init; }
    public string? LastPageUrl { get; init; }
    public string? LastReferrer { get; init; }
    public string? Language { get; init; }
}

public sealed record UpdatePersonRequest(string? Name = null, string? Email = null, string? Notes = null);

public sealed record ErasePersonRequest(bool Confirm = false);

public sealed record ErasePersonResultDto(int Conversations, int Visitors);

public sealed record PersonExportDto(
    DateTimeOffset ExportedAt,
    PersonDto Person,
    IReadOnlyList<PersonExportVisitorDto> Browsers,
    IReadOnlyList<PersonExportConversationDto> Conversations);

public sealed record PersonExportVisitorDto(Guid Id, Guid WidgetId, string? Name, string? Email, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

public sealed record PersonExportConversationDto(
    Guid Id,
    string Title,
    string Channel,
    string Status,
    Guid? WidgetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PersonExportMessageDto> Messages,
    IReadOnlyList<PersonExportFeedbackDto> Feedback,
    IReadOnlyList<PersonExportHandoverDto> Handovers);

public sealed record PersonExportMessageDto(int Sequence, string Author, string? AuthorName, string Text, DateTimeOffset CreatedAt);

public sealed record PersonExportFeedbackDto(string Kind, int Rating, string? Comment, IReadOnlyList<string> Reasons, string Author, DateTimeOffset CreatedAt);

public sealed record PersonExportHandoverDto(string Channel, string Status, string? Reason, string? Summary, DateTimeOffset RequestedAt, DateTimeOffset? ResolvedAt);

public sealed record ServerContactResult(Guid ContactId, bool Created, AttributeChangeResult Attributes);

public sealed record IdentifiedContact(Contact Contact, AttributeChangeResult Attributes);

public static class VisitorSessions
{
    public static readonly TimeSpan IdleTime = TimeSpan.FromMinutes(30);
}

public sealed record VisitorVisit(bool NewSession, string? PageUrl, string? Referrer, string? Language);

public sealed record ConversationContactDto(Guid Id, string Name, string? Email, bool Identified);

public sealed record ConversationCreatorDto(string Id, string Name);
