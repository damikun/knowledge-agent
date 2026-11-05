namespace KnowledgeAgent.Features.Evaluations.Datasets;

public sealed record CreateEvalDatasetRequest(string Name, string? Description = null, string? CopyFrom = null, string? CopyFromSource = null);

public sealed record UpdateEvalDatasetRequest(string? Name = null, string? Description = null);

public sealed record SaveEvalItemRequest(
    string Question,
    string? ReferenceAnswer = null,
    IReadOnlyList<string>? ExpectedFacts = null,
    IReadOnlyList<string>? ExpectedSources = null,
    bool Answerable = true,
    IReadOnlyList<string>? Tags = null,
    Guid? ConversationId = null,
    Guid? MessageId = null,
    string? Source = null,
    IReadOnlyList<EvalTurn>? History = null);

public sealed record ImportEvalItemsRequest(IReadOnlyList<SaveEvalItemRequest>? Items);

public sealed record ImportEvalItemsResultDto(int Created, int Skipped, IReadOnlyList<int> SkippedRows, IReadOnlyList<EvalDatasetItemDto> Items);

public sealed record DeleteEvalItemsRequest(IReadOnlyList<Guid>? Ids);

public sealed record DeleteEvalItemsResultDto(int Deleted);

public sealed record EvalDatasetItemDto(
    Guid Id,
    string Question,
    string? ReferenceAnswer,
    IReadOnlyList<string> ExpectedFacts,
    IReadOnlyList<string> ExpectedSources,
    bool Answerable,
    IReadOnlyList<string> Tags,
    Guid? ConversationId,
    Guid? MessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Source,
    IReadOnlyList<EvalTurn> History);
