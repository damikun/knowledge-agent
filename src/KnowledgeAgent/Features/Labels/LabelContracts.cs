namespace KnowledgeAgent.Features.Labels;

public sealed record LabelDto(
    Guid Id,
    string Name,
    string Color,
    string Description,
    bool AutoApply,
    int ConversationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConversationLabelDto(Guid Id, string Name, string Color, string Source);

public sealed record CreateLabelRequest(string? Name, string? Color = null, string? Description = null, bool? AutoApply = null);

public sealed record UpdateLabelRequest(string? Name = null, string? Color = null, string? Description = null, bool? AutoApply = null);

public sealed record SetConversationLabelsRequest(IReadOnlyList<Guid>? LabelIds);

public sealed record RelabelResponse(int Queued);
