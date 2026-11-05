using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Evaluations.Running;

public sealed class LiveEvalTarget(IServiceProvider services, bool keepConversations = false) : IEvalTarget
{
    internal const int CandidatesPerReference = 50;

    public string Kind => EvalTargets.Live;

    public IServiceProvider Services => services;

    public async Task<ResolvedSources> ResolveSourcesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, IReadOnlySet<Guid>>(StringComparer.OrdinalIgnoreCase);
        await using var scope = services.CreateAsyncScope();
        var sources = scope.ServiceProvider.GetRequiredService<ISourceService>();
        foreach (var reference in references.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = new HashSet<Guid>();
            if (Guid.TryParse(reference, out var id))
            {
                if (await sources.GetAsync(id, cancellationToken) is { Status: SourceStatus.Ready } byId)
                {
                    matches.Add(byId.Id);
                }
            }
            else
            {
                var page = await sources.FindAsync(
                    new SourceQuery(Text: reference, Statuses: [SourceStatus.Ready], Limit: CandidatesPerReference),
                    cancellationToken);
                matches.UnionWith(page.Items.Where(source => Matches(source, reference)).Select(source => source.Id));
            }

            resolved[reference] = matches;
        }

        return new ResolvedSources(resolved);
    }

    public async Task CleanupConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (keepConversations)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IChatService>().DeleteConversationAsync(conversationId, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static bool Matches(Source source, string reference) =>
        string.Equals(source.Title?.Trim(), reference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source.FileName, reference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source.Url?.TrimEnd('/'), reference.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
