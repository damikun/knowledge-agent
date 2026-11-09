using System.Diagnostics;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Handover;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

// Only offers a person: the widget shows its own button, and the visitor's press starts the request.
// The reason and summary stay in the tool call, where the request picks the summary up for the team.
internal sealed class RequestHumanHandoverTool : IAgentTool
{
    public string Name => ToolNames.RequestHumanHandover;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.RequestHumanHandover,
        "Offers the visitor a person from the support team: they get a button to ask for one. Use it when the visitor asks "
        + "for a person, or when you cannot help them. Nobody is asked until the visitor presses the button. The result says "
        + "what to tell the visitor.",
        ToolSchemas.RequestHumanHandover());

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        arguments.GetString("reason", required: true);
        arguments.GetString("summary", required: true);
        arguments.ThrowIfInvalid(Name);

        if (invocation.Services.GetService<IHandoverRequester>() is not { } handover)
        {
            return ToolResult.Failure("A person cannot be reached from here.");
        }

        var options = invocation.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
        if (!invocation.Turn.TryTakeBudget(TurnBudgets.Handover, options.MaxHandoverOffersPerTurn))
        {
            return TurnBudgets.LimitReached(Name, TurnBudgets.Handover);
        }

        var outcome = await handover.OfferAsync(invocation.Turn.ConversationId, cancellationToken);
        if (outcome.Offered)
        {
            GuardTelemetry.Record(Activity.Current, GuardTelemetry.Reasons.HandoverOffered);
            invocation.Turn.Events.TryWrite(new HandoverOfferedEvent(invocation.CallId) { Agent = invocation.Agent });
        }

        var summaryText = outcome switch
        {
            { Offered: true } => "Offered a person",
            { Simulated: true } => "Test chat: nobody was asked",
            { Started: true } => "A person was already asked",
            _ => "No person could be offered",
        };
        return new ToolResult(outcome.Instruction, summaryText, IsError: !outcome.Started && !outcome.Offered);
    }
}
