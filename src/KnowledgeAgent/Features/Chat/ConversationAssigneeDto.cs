namespace KnowledgeAgent.Features.Chat;

public sealed record ConversationAssigneeDto(string? Id, string Name)
{
    public static ConversationAssigneeDto? Of(string? id, string? name) => string.IsNullOrEmpty(name) ? null : new(id, name);
}
