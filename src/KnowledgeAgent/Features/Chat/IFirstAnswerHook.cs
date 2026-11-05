namespace KnowledgeAgent.Features.Chat;

// A failing hook never fails the turn.
public interface IFirstAnswerHook
{
    Task OnFirstAnswerAsync(Conversation conversation, CancellationToken cancellationToken);
}

// Verified is false for values the browser sent, which anyone can change.
public sealed record VisitorFact(string Label, string Value, bool Verified);

public interface IVisitorProfile
{
    Task<IReadOnlyList<VisitorFact>> FactsAsync(Guid contactId, CancellationToken cancellationToken);
}
