namespace KnowledgeAgent.Features.Handover;

public sealed record HandoverOutcome(bool Started, string Instruction, bool Simulated = false);

public interface IHandoverRequester
{
    Task<HandoverOutcome> RequestFromAgentAsync(Guid conversationId, string reason, string summary, CancellationToken cancellationToken = default);
}
