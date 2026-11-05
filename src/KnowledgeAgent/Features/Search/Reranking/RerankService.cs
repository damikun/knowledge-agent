using System.Collections.Concurrent;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Search.Reranking;

internal sealed partial class RerankService(
    IModelProfileResolver resolver,
    AiProviderRegistry providers,
    IAiOptionsSource optionsSource,
    IChatClientFactory chatClients,
    IServiceProvider services,
    ILogger<RerankService> logger) : IRerankService, IDisposable
{
    public const string ProfileName = "rerank";

    private readonly ConcurrentDictionary<string, Lazy<IReranker>> _rerankers = new(StringComparer.Ordinal);

    public async Task<string?> DescribeAsync(CancellationToken cancellationToken = default) =>
        await ResolveAsync(cancellationToken) is { } target ? Label(target.Profile) : null;

    public async Task<RerankResult?> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(documents);
        if (await ResolveAsync(cancellationToken) is not { } target)
        {
            return null;
        }

        if (documents.Count == 0)
        {
            return new RerankResult(Label(target.Profile), []);
        }

        var scores = target.Provider.SupportsRerank
            ? await GetReranker(target).RerankAsync(query, documents, cancellationToken)
            : await LlmRerankAsync(query, documents, cancellationToken);
        if (scores.Count != documents.Count)
        {
            throw new InvalidOperationException($"The reranker returned {scores.Count} scores for {documents.Count} documents.");
        }

        return new RerankResult(Label(target.Profile), scores.Select(score => double.IsFinite(score) ? Math.Clamp(score, 0d, 1d) : 0d).ToList());
    }

    public void Dispose()
    {
        foreach (var lazy in _rerankers.Values.Where(lazy => lazy.IsValueCreated))
        {
            (lazy.Value as IDisposable)?.Dispose();
        }

        _rerankers.Clear();
    }

    private async Task<IReadOnlyList<double>> LlmRerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        var prompts = services.GetService<IPromptService>()
            ?? throw new InvalidOperationException("Reranking with a chat model needs the prompts module (the 'rerank' prompt).");
        var model = await chatClients.CreateAsync(ProfileName, cancellationToken: cancellationToken);
        return await new LlmReranker(model, prompts).RerankAsync(query, documents, cancellationToken);
    }

    private IReranker GetReranker(Target target)
    {
        var key = ClientCacheKey.Create(target.Profile.ProviderKey, target.Provider.Type, target.Profile.Model, target.Options, "rerank");
        var lazy = _rerankers.GetOrAdd(key, _ => new Lazy<IReranker>(() => target.Provider.CreateReranker(target.Options, target.Profile)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            _rerankers.TryRemove(new KeyValuePair<string, Lazy<IReranker>>(key, lazy));
            throw;
        }
    }

    private async Task<Target?> ResolveAsync(CancellationToken cancellationToken)
    {
        ResolvedModelProfile profile;
        try
        {
            profile = await resolver.ResolveAsync(ProfileName, cancellationToken: cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            LogNoReranker(logger, ex.Message);
            return null;
        }

        if (profile.Kind != ModelKind.Rerank)
        {
            LogWrongKind(logger, ProfileName, profile.Kind);
            return null;
        }

        var options = await optionsSource.GetCurrentAsync(cancellationToken);
        return options.Providers.TryGetValue(profile.ProviderKey, out var providerOptions) && providers.Find(providerOptions.Type) is { } provider
            ? new Target(profile, provider, providerOptions)
            : null;
    }

    private static string Label(ResolvedModelProfile profile) => $"{profile.ProviderKey}/{profile.Model}";

    [LoggerMessage(Level = LogLevel.Debug, Message = "No reranker: {Reason}")]
    private static partial void LogNoReranker(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The '{Profile}' profile has Kind = {Kind}; it must be Rerank to rerank search results")]
    private static partial void LogWrongKind(ILogger logger, string profile, ModelKind kind);

    private sealed record Target(ResolvedModelProfile Profile, IAiProvider Provider, AiProviderOptions Options);
}
