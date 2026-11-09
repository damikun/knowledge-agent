using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.People;

namespace KnowledgeAgent.Features.Chat;

public sealed record StopTurnResponse(bool Stopped);

public sealed record ChatRequestDto(
    Guid? ConversationId,
    string Message,
    IReadOnlyList<Guid>? SourceIds = null,
    string? Profile = null,
    IReadOnlyDictionary<string, JsonElement>? Variables = null,
    string? Agent = null,
    Guid? WidgetId = null,
    ToolConfirmationRequest? Confirmation = null);

public sealed record ToolConfirmationRequest(string CallId, bool Approved);

public static class ChatEventTypes
{
    public const string Conversation = "conversation";
    public const string AgentStarted = "agent-started";
    public const string TextDelta = "text-delta";
    public const string ReasoningDelta = "reasoning-delta";
    public const string ToolCall = "tool-call";
    public const string ToolResult = "tool-result";
    public const string Usage = "usage";
    public const string AgentCompleted = "agent-completed";
    public const string Error = "error";
    public const string Done = "done";
    public const string ConfirmationRequired = "confirmation-required";
    public const string AgentTransfer = "agent-transfer";
    public const string HandoverOffered = "handover-offered";
    public const string StepFinished = "step-finished";
}

public abstract record ChatStreamEvent
{
    [JsonIgnore]
    public abstract string EventType { get; }

    public string Agent { get; init; } = "orchestrator";

    public string? ParentCallId { get; init; }
}

public sealed record ConversationEvent(Guid ConversationId, string Title, bool IsNew) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.Conversation;
}

public sealed record AgentStartedEvent(string Provider, string Model) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.AgentStarted;
}

public sealed record TextDeltaEvent(string Text) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.TextDelta;

    // The model call that wrote the text; numbered per turn, so a new number starts a new paragraph.
    public int Step { get; init; }
}

// Ends one model call. "interim": tools run next, so its text was a note on the way and is not the answer.
// "final": its text is the agent's answer. Clients that ignore this event see every step's text, as before.
public sealed record StepFinishedEvent(int Step, string Kind) : ChatStreamEvent
{
    public const string Interim = "interim";
    public const string Final = "final";

    [JsonIgnore]
    public override string EventType => ChatEventTypes.StepFinished;
}

public sealed record ReasoningDeltaEvent(string Text) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.ReasoningDelta;
}

public sealed record ToolCallEvent(string CallId, string ToolName, JsonElement? Arguments) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.ToolCall;
}

public sealed record ToolResultEvent(
    string CallId,
    string ToolName,
    bool IsError,
    string Summary,
    long DurationMs,
    IReadOnlyList<CitationDto> Citations) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.ToolResult;
}

public sealed record ConfirmationRequiredEvent(
    string CallId,
    string ToolName,
    string Title,
    string? Connection,
    JsonElement? Arguments,
    DateTimeOffset ExpiresAt) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.ConfirmationRequired;

    public IReadOnlyList<ConfirmationDetailDto> Details { get; init; } = [];
}

// One argument of an action that waits for Confirm, in words the person can check.
public sealed record ConfirmationDetailDto(string Label, string Value);

// The agent offered a person; the visitor asks for one with the widget's own button.
public sealed record HandoverOfferedEvent(string CallId) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.HandoverOffered;
}

public sealed record AgentTransferEvent(string CallId, Guid? ToAgentId, string ToAgent, string ToAgentName, string Reason, bool Back) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.AgentTransfer;
}

public sealed record UsageEvent(string Provider, string Model, long? InputTokens, long? OutputTokens, long? TotalTokens) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.Usage;
}

public sealed record AgentCompletedEvent(string FinishReason, int Iterations) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.AgentCompleted;
}

public sealed record ErrorEvent(string Message, string? Code = null) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.Error;
}

public sealed record DoneEvent(Guid ConversationId, Guid TurnId, string FinishReason) : ChatStreamEvent
{
    [JsonIgnore]
    public override string EventType => ChatEventTypes.Done;
}

public sealed record CitationDto(
    string Ref,
    Guid SourceId,
    Guid ChunkId,
    string SourceTitle,
    string? SourceUrl,
    int? Page,
    string Snippet,
    double Score);

public sealed record ToolCallDto(
    string CallId,
    string ToolName,
    string Agent,
    string? ParentCallId,
    JsonElement? Arguments,
    string Summary,
    bool IsError,
    long DurationMs)
{
    public string? Result { get; init; }

    public ToolCallConfirmationDto? Confirmation { get; init; }

    // What the agent wrote before this call, on the first call of a step only.
    public string? Preamble { get; init; }
}

public sealed record ToolCallConfirmationDto(string Status, string Title, string? Connection, DateTimeOffset ExpiresAt)
{
    public IReadOnlyList<ConfirmationDetailDto>? Details { get; init; }

    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Declined = "declined";
}

public sealed record ConversationSummaryDto(
    Guid Id,
    string Title,
    int TurnCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Channel = "portal",
    string Status = "ai",
    Guid? AgentId = null,
    Guid? WidgetId = null,
    string? Preview = null,
    string? HandoverStatus = null)
{
    public bool Pinned { get; init; }
    public IReadOnlyList<ConversationLabelDto> Labels { get; init; } = [];

    public ConversationContactDto? Contact { get; init; }
    public ConversationCreatorDto? CreatedBy { get; init; }

    public ConversationAssigneeDto? Assignee { get; init; }
    public bool Unread { get; init; }

    public bool Test => Channel == "portal";

    public ConversationActiveAgentDto? ActiveAgent { get; init; }
}

public sealed record ConversationActiveAgentDto(Guid Id, string Name)
{
    public static ConversationActiveAgentDto? Of(Guid? id, string? name) => id is { } agentId ? new(agentId, name ?? string.Empty) : null;
}

public sealed record ConversationDto(
    Guid Id,
    string Title,
    string AgentName,
    string? PromptName,
    int? PromptVersion,
    string? PromptOrigin,
    string? Profile,
    IReadOnlyList<Guid> SourceIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConversationTurnDto> Turns,
    Guid? AgentId = null,
    string Channel = "portal",
    string Status = "ai",
    IReadOnlyList<ConversationMessageDto>? Messages = null,
    Guid? WidgetId = null,
    Guid? VisitorId = null)
{
    public bool Pinned { get; init; }

    public Guid? ContactId { get; init; }
    public ConversationContactDto? Contact { get; init; }
    public ConversationCreatorDto? CreatedBy { get; init; }

    public ConversationAssigneeDto? Assignee { get; init; }

    public ConversationActiveAgentDto? ActiveAgent { get; init; }
}

public sealed record ConversationMessageDto(
    Guid Id,
    int Sequence,
    string Author,
    string? AuthorName,
    string Text,
    IReadOnlyList<CitationDto> Citations,
    Guid? TurnId,
    DateTimeOffset CreatedAt)
{
    public bool StaffOnly { get; init; }
}

public sealed record ConversationTurnDto(
    Guid Id,
    int Sequence,
    string UserMessage,
    string AssistantMessage,
    IReadOnlyList<ToolCallDto> ToolCalls,
    IReadOnlyList<CitationDto> Citations,
    string? Provider,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    string? FinishReason,
    string? Error,
    long DurationMs,
    DateTimeOffset CreatedAt,
    string? TraceId = null);
