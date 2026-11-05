using KnowledgeAgent.Features.Chat.Agents;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Tools;

internal interface IAgentTool
{
    string Name { get; }

    AIFunctionDeclaration Declaration { get; }

    TimeSpan? Timeout => null;

    Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}

internal sealed record ToolInvocation(
    string CallId,
    ToolArguments Arguments,
    AgentTurnContext Turn,
    string Agent,
    AgentRunner Runner,
    IServiceProvider Services)
{
    public bool Confirmed { get; init; }
}

internal sealed record ToolResult(string Text, string Summary, bool IsError = false, IReadOnlyList<CitationDto>? Citations = null)
{
    public string? LogText { get; init; }

    public ToolConfirmationPrompt? Confirmation { get; init; }

    public bool EndsRun { get; init; }

    public static ToolResult Failure(string message) => new("Error: " + message, message, IsError: true);
}

internal sealed class ToolArgumentException(string message) : Exception(message);

internal sealed record ToolConfirmationPrompt(string Title, string? Connection);

internal interface IAgentToolSource
{
    Task<IReadOnlyList<IAgentTool>> ResolveAsync(KnowledgeAgent.Features.Agents.AgentConfig agent, ChatCaller caller, CancellationToken cancellationToken);
}
