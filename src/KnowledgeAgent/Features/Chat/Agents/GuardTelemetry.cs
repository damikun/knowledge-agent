using System.Diagnostics;
using System.Diagnostics.Metrics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Chat.Agents;

// Blocked or suspicious events around untrusted content, as span tags and events plus one counter.
internal static class GuardTelemetry
{
    public const string EventName = "knowledge_agent.guard";

    private static readonly Meter Meter = new(KnowledgeAgentTelemetry.Agents);

    public static readonly Counter<long> Events = Meter.CreateCounter<long>(
        "knowledge_agent.guard.events", unit: "{event}", description: "Tool calls blocked or flagged by the prompt-injection guards, by reason.");

    public static class Tags
    {
        public const string Reason = "knowledge_agent.guard.reason";
        public const string Detail = "knowledge_agent.guard.detail";
        public const string AfterUntrusted = "knowledge_agent.tool.after_untrusted";
        public const string UntrustedResults = "knowledge_agent.untrusted.count";
    }

    public static class Reasons
    {
        public const string TurnLimit = "turn_limit";
        public const string ConversationLimit = "conversation_limit";
        public const string McpDefinitionChanged = "mcp_definition_changed";
        public const string McpNotApproved = "mcp_not_approved";
        public const string HandoverOffered = "handover_offered";
    }

    public static void Record(Activity? activity, string reason, string? detail = null)
    {
        Events.Add(1, new KeyValuePair<string, object?>(Tags.Reason, reason));
        if (activity is null)
        {
            return;
        }

        var tags = new ActivityTagsCollection { [Tags.Reason] = reason };
        if (detail is not null)
        {
            tags[Tags.Detail] = detail;
        }

        activity.AddEvent(new ActivityEvent(EventName, tags: tags));
    }

    // How much untrusted content the model had seen when it called the tool.
    public static void SetContext(Activity? activity, int untrustedResults)
    {
        activity?.SetTag(Tags.AfterUntrusted, untrustedResults > 0);
        activity?.SetTag(Tags.UntrustedResults, untrustedResults);
    }
}
