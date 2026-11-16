namespace KnowledgeAgent.Features.Chat;

public interface IFirstAnswerHook
{
    Task OnFirstAnswerAsync(Conversation conversation, CancellationToken cancellationToken);
}

public sealed record VisitorFact(string Label, string Value, bool Verified);

public interface IVisitorProfile
{
    Task<IReadOnlyList<VisitorFact>> FactsAsync(Guid contactId, CancellationToken cancellationToken);
}
