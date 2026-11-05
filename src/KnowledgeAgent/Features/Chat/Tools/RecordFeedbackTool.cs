using KnowledgeAgent.Features.Feedback;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class RecordFeedbackTool : IAgentTool
{
    public string Name => ToolNames.RecordFeedback;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.RecordFeedback,
        "Saves feedback that the person gives about the service, your answers or you, so the team sees it with this "
        + "conversation. Use it when they want to give feedback, praise or complain, not for ordinary questions.",
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

        await recorder.RecordFromAgentAsync(invocation.Turn.ConversationId, rating, comment, cancellationToken);
        return new ToolResult("Feedback recorded for the team. Thank the person briefly.", "Feedback recorded");
    }
}
