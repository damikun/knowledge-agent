using Inngest;

namespace KnowledgeAgent.Infrastructure.Jobs;

internal sealed class InngestJobQueue(IInngestClient client, IEnumerable<JobRegistration> registrations, TimeProvider timeProvider) : IJobQueue
{
    private readonly HashSet<string> _kinds = registrations.Select(registration => registration.Kind).ToHashSet(StringComparer.Ordinal);

    public async Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
    {
        if (!_kinds.Contains(kind))
        {
            throw new InvalidOperationException($"No job handler is registered for '{kind}'.");
        }

        // An event timestamp in the future makes Inngest start the run at that time.
        var due = runAt is { } requested && requested > timeProvider.GetUtcNow() ? requested : (DateTimeOffset?)null;
        await client.SendAsync(new InngestEvent(InngestJobEngine.EventName(kind), new JobEvent(key)) { Timestamp = due }, cancellationToken);
    }
}
