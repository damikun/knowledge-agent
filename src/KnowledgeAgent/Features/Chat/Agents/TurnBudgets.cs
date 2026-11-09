using System.Diagnostics;
using KnowledgeAgent.Features.Chat.Tools;

namespace KnowledgeAgent.Features.Chat.Agents;

// Caps on tools with side effects in one turn, sub-agents included. Reading tools are not capped here.
internal static class TurnBudgets
{
    public const string Handover = "handover";
    public const string Feedback = "feedback";
    public const string McpConfirm = "mcp_confirm";
    public const string McpCall = "mcp_call";
    public const string McpAuto = "mcp_auto";

    public static ToolResult LimitReached(string toolName, string kind)
    {
        GuardTelemetry.Record(Activity.Current, GuardTelemetry.Reasons.TurnLimit, kind);
        return ToolResult.Failure($"{toolName} cannot run again in this turn: the limit for this turn was reached. Answer with what you have.");
    }
}
