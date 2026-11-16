namespace KnowledgeAgent.Features.Chat;

public enum ConversationChannel
{
    Portal,
    Widget,

    Api,
    Mcp,
}

public enum ConversationStatus
{
    Ai,

    HandoverPending,

    Human,

    Closed,
}

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = "New conversation";
    public string AgentName { get; set; } = "orchestrator";

    public Guid? AgentId { get; set; }

    public Guid? ActiveAgentId { get; set; }
    public string? ActiveAgentName { get; set; }

    public ConversationChannel Channel { get; set; } = ConversationChannel.Portal;
    public ConversationStatus Status { get; set; } = ConversationStatus.Ai;

    public Guid? WidgetId { get; set; }
    public Guid? VisitorId { get; set; }

    public Guid? ContactId { get; set; }

    public string? CreatedById { get; set; }
    public string? CreatedByName { get; set; }

    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string? PromptName { get; set; }
    public int? PromptVersion { get; set; }
    public string? PromptOrigin { get; set; }
    public string? Profile { get; set; }
    public List<Guid> SourceIds { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Pinned { get; set; }

    public DateTimeOffset? AutoLabeledAt { get; set; }

    public List<ConversationTurn> Turns { get; set; } = new();
    public List<ConversationMessage> Messages { get; set; } = new();
}

public sealed class ConversationTurn
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ConversationId { get; set; }
    public int Sequence { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public string AssistantMessage { get; set; } = string.Empty;
    public string ModelMessagesJson { get; set; } = "[]";
    public string ToolCallsJson { get; set; } = "[]";
    public string CitationsJson { get; set; } = "[]";
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public string? FinishReason { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }

    public string? TraceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
