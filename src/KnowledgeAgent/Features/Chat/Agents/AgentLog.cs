using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Chat.Agents;

// Log messages never include prompt or answer text.
internal static partial class AgentLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Chat turn {TurnId} started for conversation {ConversationId} (new: {IsNew}, profile: {Profile})")]
    public static partial void TurnStarted(ILogger logger, Guid turnId, Guid conversationId, bool isNew, string profile);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Chat turn {TurnId} of conversation {ConversationId} finished: {FinishReason} after {Iterations} model call(s) in {DurationMs} ms")]
    public static partial void TurnCompleted(ILogger logger, Guid turnId, Guid conversationId, string finishReason, int iterations, long durationMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Chat turn {TurnId} of conversation {ConversationId} failed with {ErrorCode}")]
    public static partial void TurnFailed(ILogger logger, Exception? exception, Guid turnId, Guid conversationId, string errorCode);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information,
        Message = "Chat turn for conversation {ConversationId} was cancelled by the client")]
    public static partial void TurnCancelled(ILogger logger, Guid? conversationId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Conversation {ConversationId} was not found")]
    public static partial void ConversationNotFound(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Chat turn setup failed for conversation {ConversationId}")]
    public static partial void TurnSetupFailed(ILogger logger, Exception exception, Guid? conversationId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Could not persist chat turn {TurnId} of conversation {ConversationId}")]
    public static partial void TurnPersistFailed(ILogger logger, Exception exception, Guid turnId, Guid conversationId);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Skipping unreadable model messages of turn {TurnId} in conversation {ConversationId}")]
    public static partial void HistoryTurnSkipped(ILogger logger, Exception exception, Guid turnId, Guid conversationId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning,
        Message = "Could not read the config of prompt {PromptName}; using the profile without prompt settings")]
    public static partial void PromptConfigUnavailable(ILogger logger, Exception exception, string promptName);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning, Message = "Model profile {Profile} is not configured: {Reason}")]
    public static partial void ModelNotConfigured(ILogger logger, string profile, string reason);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Could not save the title of conversation {ConversationId}")]
    public static partial void TitleSaveFailed(ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning,
        Message = "Content of type {ContentType} in turn {TurnId} cannot be serialized and was left out of the replayed history")]
    public static partial void ContentNotPersisted(ILogger logger, string contentType, Guid turnId);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "A first-answer hook failed for conversation {ConversationId}")]
    public static partial void FirstAnswerHookFailed(ILogger logger, Exception exception, Guid conversationId);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information,
        Message = "Conversation {ConversationId} was handed from agent {FromAgent} to agent {ToAgent}")]
    public static partial void HandedOff(ILogger logger, Guid conversationId, string fromAgent, string toAgent);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning,
        Message = "Model call {Iteration} of agent {Agent} failed")]
    public static partial void ModelCallFailed(ILogger logger, Exception exception, string agent, int iteration);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Agent {Agent} reached its limit of {MaxIterations} tool round(s); requesting a final answer without tools")]
    public static partial void MaxIterationsReached(ILogger logger, string agent, int maxIterations);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "Agent {Agent} received a refusal (content filter) on model call {Iteration}")]
    public static partial void Refused(ILogger logger, string agent, int iteration);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "Turn {TurnId} in conversation {ConversationId} ended without a final text; the last step's text is stored as the answer")]
    public static partial void AnswerFromInterim(ILogger logger, Guid turnId, Guid conversationId);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Tool {ToolName} (call {CallId}) failed")]
    public static partial void ToolFailed(ILogger logger, Exception exception, string toolName, string callId);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning,
        Message = "Tool {ToolName} (call {CallId}) timed out after {TimeoutSeconds} s")]
    public static partial void ToolTimedOut(ILogger logger, string toolName, string callId, double timeoutSeconds);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug,
        Message = "Tool call {CallId} of agent {Agent} rejected: {Reason}")]
    public static partial void ToolCallRejected(ILogger logger, string callId, string agent, string reason);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Title generation failed; using the message prefix")]
    public static partial void TitleGenerationFailed(ILogger logger, Exception exception);
}
