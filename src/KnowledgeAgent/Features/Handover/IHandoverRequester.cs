namespace KnowledgeAgent.Features.Handover;

// Offered: the visitor sees a button and nobody is asked until they press it.
public sealed record HandoverOutcome(bool Started, string Instruction, bool Simulated = false, bool Offered = false);

public interface IHandoverRequester
{
    // The agent can only offer a person; the request itself comes from the visitor's button.
    Task<HandoverOutcome> OfferAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
