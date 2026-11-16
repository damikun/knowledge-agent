namespace KnowledgeAgent.Features.Handover;

public sealed record HandoverOutcome(bool Started, string Instruction, bool Simulated = false, bool Offered = false);

public interface IHandoverRequester
{
    Task<HandoverOutcome> OfferAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
