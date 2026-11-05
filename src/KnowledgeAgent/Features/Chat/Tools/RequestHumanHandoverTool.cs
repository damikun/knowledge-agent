using KnowledgeAgent.Features.Handover;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class RequestHumanHandoverTool : IAgentTool
{
    public string Name => ToolNames.RequestHumanHandover;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.RequestHumanHandover,
        "Asks a person from the support team to take over this conversation. Use it when the visitor asks for a person, "
        + "or when you cannot help and they agree to be connected. The result says what happens next; tell the visitor "
        + "exactly that and do not answer further questions until a person has replied.",
        ToolSchemas.RequestHumanHandover());

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var reason = arguments.GetString("reason", required: true)?.Trim();
        var summary = arguments.GetString("summary", required: true)?.Trim();
        arguments.ThrowIfInvalid(Name);

        if (invocation.Services.GetService<IHandoverRequester>() is not { } handover)
        {
            return ToolResult.Failure("A person cannot be reached from here.");
        }

        var outcome = await handover.RequestFromAgentAsync(invocation.Turn.ConversationId, reason ?? string.Empty, summary ?? string.Empty, cancellationToken);
        var summaryText = outcome.Simulated ? "Test chat: nobody was asked" : outcome.Started ? "A person was asked to join" : "No person could be asked";
        return new ToolResult(outcome.Instruction, summaryText, IsError: !outcome.Started);
    }
}
