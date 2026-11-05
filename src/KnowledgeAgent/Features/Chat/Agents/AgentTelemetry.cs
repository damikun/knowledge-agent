using System.Diagnostics;
using System.Text.Json;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Chat.Agents;

internal static class AgentTelemetry
{
    public const string ChatTurnSpanName = KnowledgeAgentTelemetry.ChatTurnSpanName;
    public const string ChatTurnTraceName = "chat-turn";

    public static readonly ActivitySource Source = new(KnowledgeAgentTelemetry.Agents);

    private const int MaxAttributeLength = 8_000;

    public static class Tags
    {
        public const string TraceName = "langfuse.trace.name";
        public const string SessionId = "langfuse.session.id";
        public const string TraceInput = "langfuse.trace.input";
        public const string TraceOutput = "langfuse.trace.output";
        public const string ObservationType = "langfuse.observation.type";
        public const string ObservationInput = "langfuse.observation.input";
        public const string ObservationOutput = "langfuse.observation.output";
        public const string OperationName = "gen_ai.operation.name";
        public const string AgentName = "gen_ai.agent.name";
        public const string ProviderName = "gen_ai.provider.name";
        public const string RequestModel = "gen_ai.request.model";
        public const string InputTokens = "gen_ai.usage.input_tokens";
        public const string OutputTokens = "gen_ai.usage.output_tokens";
        public const string ToolName = "gen_ai.tool.name";
        public const string ToolCallId = "gen_ai.tool.call.id";
        public const string ConversationId = "gen_ai.conversation.id";
        public const string TurnId = "knowledge_agent.turn.id";
        public const string Profile = "knowledge_agent.profile";
        public const string PromptName = "knowledge_agent.prompt.name";
        public const string PromptVersion = "knowledge_agent.prompt.version";
        public const string ParentCallId = "knowledge_agent.parent_call_id";
        public const string FinishReason = "knowledge_agent.finish_reason";
        public const string Iterations = "knowledge_agent.iterations";
        public const string ToolIsError = "knowledge_agent.tool.is_error";
        public const string ToolDurationMs = "knowledge_agent.tool.duration_ms";
        public const string AgentId = "knowledge_agent.agent.id";
        public const string AgentDisplayName = "knowledge_agent.agent.display_name";
    }

    // Callers must clear Activity.Current first: StartActivity parents to it even when parentContext is default.
    public static Activity? StartChatTurn(Activity? previous, string agentName, string userMessage)
    {
        ActivityLink[]? links = previous is null ? null : [new ActivityLink(previous.Context)];
        var activity = Source.StartActivity(ChatTurnSpanName, ActivityKind.Internal, parentContext: default, tags: null, links: links);
        if (activity is null)
        {
            return null;
        }

        var input = TextUtil.Clip(userMessage, MaxAttributeLength);
        activity.SetTag(Tags.TraceName, ChatTurnTraceName);
        activity.SetTag(Tags.ObservationType, "agent");
        activity.SetTag(Tags.TraceInput, input);
        activity.SetTag(Tags.ObservationInput, input);
        activity.SetTag(Tags.OperationName, "invoke_agent");
        activity.SetTag(Tags.AgentName, agentName);
        return activity;
    }

    public static void SetConversation(Activity? activity, Guid conversationId, Guid turnId, string profile, string? promptName, int? promptVersion)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(Tags.SessionId, conversationId.ToString("D"));
        activity.SetTag(Tags.ConversationId, conversationId.ToString("D"));
        activity.SetTag(Tags.TurnId, turnId.ToString("D"));
        activity.SetTag(Tags.Profile, profile);
        activity.SetTag(Tags.PromptName, promptName);
        activity.SetTag(Tags.PromptVersion, promptVersion);
    }

    public static void SetModel(Activity? activity, ResolvedModelProfile profile)
    {
        activity?.SetTag(Tags.ProviderName, profile.ProviderType);
        activity?.SetTag(Tags.RequestModel, profile.Model);
    }

    public static void CompleteChatTurn(
        Activity? activity,
        string answer,
        string finishReason,
        int iterations,
        UsageTotals usage,
        AgentRunError? error)
    {
        if (activity is null)
        {
            return;
        }

        var output = TextUtil.Clip(answer, MaxAttributeLength);
        activity.SetTag(Tags.TraceOutput, output);
        activity.SetTag(Tags.ObservationOutput, output);
        activity.SetTag(Tags.FinishReason, finishReason);
        activity.SetTag(Tags.Iterations, iterations);
        activity.SetTag(Tags.InputTokens, usage.InputTokens);
        activity.SetTag(Tags.OutputTokens, usage.OutputTokens);
        if (error is not null)
        {
            RecordError(activity, error.Code, error.Message, error.Exception);
        }
    }

    public static Activity? StartTool(string toolName, string callId, string agentName, JsonElement arguments)
    {
        var activity = Source.StartActivity("tool " + toolName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(Tags.ObservationType, "tool");
        activity.SetTag(Tags.OperationName, "execute_tool");
        activity.SetTag(Tags.ToolName, toolName);
        activity.SetTag(Tags.ToolCallId, callId);
        activity.SetTag(Tags.AgentName, agentName);
        activity.SetTag(Tags.ObservationInput, TextUtil.Clip(arguments.GetRawText(), MaxAttributeLength));
        return activity;
    }

    public static void CompleteTool(Activity? activity, string output, bool isError, long durationMs)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(Tags.ObservationOutput, TextUtil.Clip(output, MaxAttributeLength));
        activity.SetTag(Tags.ToolIsError, isError);
        activity.SetTag(Tags.ToolDurationMs, durationMs);
        if (isError)
        {
            activity.SetStatus(ActivityStatusCode.Error, TextUtil.Clip(output, 500));
        }
    }

    public static Activity? StartSubAgent(
        AgentDefinition agent,
        string parentCallId,
        string task,
        ResolvedModelProfile profile,
        string promptName,
        int promptVersion)
    {
        var activity = Source.StartActivity("agent " + agent.Name, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(Tags.ObservationType, "agent");
        activity.SetTag(Tags.OperationName, "invoke_agent");
        activity.SetTag(Tags.AgentName, agent.Name);
        activity.SetTag(Tags.ParentCallId, parentCallId);
        activity.SetTag(Tags.ObservationInput, TextUtil.Clip(task, MaxAttributeLength));
        activity.SetTag(Tags.Profile, profile.Name);
        activity.SetTag(Tags.ProviderName, profile.ProviderType);
        activity.SetTag(Tags.RequestModel, profile.Model);
        activity.SetTag(Tags.PromptName, promptName);
        activity.SetTag(Tags.PromptVersion, promptVersion);
        return activity;
    }

    public static Activity? StartHandoffAgent(string agentName, string displayName, Guid? agentId, string reason)
    {
        var activity = Source.StartActivity("agent " + agentName, ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(Tags.ObservationType, "agent");
        activity.SetTag(Tags.OperationName, "invoke_agent");
        activity.SetTag(Tags.AgentName, agentName);
        activity.SetTag(Tags.AgentDisplayName, displayName);
        activity.SetTag(Tags.AgentId, agentId?.ToString("D"));
        activity.SetTag(Tags.ObservationInput, TextUtil.Clip(reason, MaxAttributeLength));
        return activity;
    }

    public static void CompleteAgent(Activity? activity, AgentRunResult result)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(Tags.ObservationOutput, TextUtil.Clip(result.FinalText.Length > 0 ? result.FinalText : result.FullText, MaxAttributeLength));
        activity.SetTag(Tags.FinishReason, result.FinishReason);
        activity.SetTag(Tags.Iterations, result.Iterations);
        if (result.Error is { } error)
        {
            RecordError(activity, error.Code, error.Message, error.Exception);
        }
    }

    public static void RecordError(Activity? activity, string code, string message, Exception? exception = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, message);
        activity.SetTag("error.type", code);
        if (exception is not null)
        {
            activity.AddException(exception);
        }
    }
}
