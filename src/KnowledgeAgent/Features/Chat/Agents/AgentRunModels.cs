using KnowledgeAgent.Infrastructure.AI.Chat;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed record PromptReference(string Name, int? Version);

internal sealed record AgentRunRequest(
    AgentDefinition Agent,
    ChatModelHandle Model,
    List<ChatMessage> Messages,
    PromptReference Prompt,
    AgentTurnContext Turn,
    string? ParentCallId = null);

internal sealed record AgentRunError(string Code, string Message, Exception? Exception = null);

internal sealed record AgentRunResult(
    string FinishReason,
    int Iterations,
    string FinalText,
    string FullText,
    IReadOnlyList<CitationDto> Citations,
    AgentRunError? Error);
