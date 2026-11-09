using System.Diagnostics;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Feedback;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class RecordFeedbackTool : IAgentTool
{
    public string Name => ToolNames.RecordFeedback;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.RecordFeedback,
        "Notes feedback that the person gives in their own message about the service, your answers or you, so the team sees it "
        + "with this conversation as a note from you. Use it when they give feedback, praise or complain, not for ordinary questions.",
        ToolSchemas.RecordFeedback());

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var sentiment = arguments.GetString("sentiment", required: true);
        var comment = arguments.GetString("comment", required: true)?.Trim();
        arguments.ThrowIfInvalid(Name);

        var rating = sentiment?.Trim().ToLowerInvariant() switch
        {
            "positive" => 1,
            "neutral" => 0,
            "negative" => -1,
            _ => throw new ToolArgumentException($"Invalid arguments for {Name}: 'sentiment' must be positive, neutral or negative."),
        };
        if (string.IsNullOrEmpty(comment))
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'comment' must say what the feedback is.");
        }

        if (invocation.Services.GetService<IFeedbackRecorder>() is not { } recorder)
        {
            return ToolResult.Failure("Feedback cannot be recorded here.");
        }

        var options = invocation.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
        if (!invocation.Turn.TryTakeBudget(TurnBudgets.Feedback, options.MaxFeedbackPerTurn))
        {
            return TurnBudgets.LimitReached(Name, TurnBudgets.Feedback);
        }

        if (await recorder.RecordFromAgentAsync(invocation.Turn.ConversationId, rating, comment, options.MaxFeedbackPerConversation, cancellationToken) is null)
        {
            GuardTelemetry.Record(Activity.Current, GuardTelemetry.Reasons.ConversationLimit, TurnBudgets.Feedback);
            return new ToolResult(
                "The team already has enough notes from this conversation, so this one was not saved. Thank the person briefly.",
                "Feedback not saved: limit reached");
        }

        return new ToolResult("Feedback noted for the team. Thank the person briefly.", "Feedback recorded");
    }
}
