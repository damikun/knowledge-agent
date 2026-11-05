using System.Collections.Concurrent;
using System.Diagnostics;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Prompts.Rendering;
using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Prompts;

internal sealed class PromptService : IPromptService, IDisposable
{
    internal static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromSeconds(10);

    private const string LatestLabel = "latest";
    private const int CacheCapacity = 512;
    private const int ListConcurrency = 4;

    private readonly IPromptTemplateRenderer _renderer;
    private readonly LocalPromptCatalog _local;
    private readonly LangfuseClient _langfuse;
    private readonly LangfuseLinkBuilder _links;
    private readonly PromptOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PromptService> _logger;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _failureBackoff;
    private readonly string _defaultLabel;
    private readonly LruCache<CacheKey, CacheEntry> _cache = new(CacheCapacity);
    private readonly ConcurrentDictionary<CacheKey, Task<PromptTemplate?>> _inflight = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private int _disposed;
    private long _generation;

    public PromptService(
        IPromptTemplateRenderer renderer,
        LocalPromptCatalog local,
        LangfuseClient langfuse,
        LangfuseLinkBuilder links,
        IOptions<PromptOptions> promptOptions,
        IOptions<LangfuseOptions> langfuseOptions,
        TimeProvider timeProvider,
        ILogger<PromptService> logger)
    {
        _renderer = renderer;
        _local = local;
        _langfuse = langfuse;
        _links = links;
        _options = promptOptions.Value;
        _time = timeProvider;
        _logger = logger;
        UsesLangfuse = PromptStoreMode.UsesLangfuse(_options, langfuseOptions.Value);
        _ttl = TimeSpan.FromSeconds(Math.Max(0, _options.CacheTtlSeconds));
        _failureBackoff = _ttl < MaxFailureBackoff ? _ttl : MaxFailureBackoff;
        _defaultLabel = string.IsNullOrWhiteSpace(_options.Label) ? LangfuseClient.DefaultLabel : _options.Label.Trim();

        // Captured so that fetches started after disposal see a cancelled token.
        _shutdownToken = _shutdown.Token;
    }

    internal bool UsesLangfuse { get; }

    public async Task<PromptTemplate> GetTemplateAsync(string name, PromptSelector? selector = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!UsesLangfuse)
        {
            return _local.TryGet(name, out var local) ? local : throw new PromptNotFoundException(name);
        }

        var key = CacheKey.Create(name, selector, _defaultLabel);
        var entry = await GetEntryAsync(key, cancellationToken).ConfigureAwait(false);
        return Resolve(key, entry);
    }

    public async Task<RenderedPrompt> RenderAsync(
        string name,
        IReadOnlyDictionary<string, object?> variables,
        PromptSelector? selector = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var template = await GetTemplateAsync(name, selector, cancellationToken).ConfigureAwait(false);

        using var activity = PromptsTelemetry.ActivitySource.StartActivity(PromptsTelemetry.RenderActivityName);
        activity?.SetTag(PromptsTelemetry.PromptNameTag, template.Name);
        activity?.SetTag(PromptsTelemetry.PromptVersionTag, template.Version);
        activity?.SetTag(PromptsTelemetry.PromptOriginTag, template.Origin.ToString());
        activity?.SetTag(PromptsTelemetry.PromptTypeTag, template.Type.ToString());

        try
        {
            string? text = null;
            IReadOnlyList<PromptMessage> messages = [];
            if (template.Type == PromptType.Chat)
            {
                messages = template.Messages
                    .Select(message => message with { Content = _renderer.Render(message.Content, variables) })
                    .ToArray();
            }
            else
            {
                text = _renderer.Render(template.Text ?? string.Empty, variables);
            }

            return new RenderedPrompt
            {
                Name = template.Name,
                Version = template.Version,
                Origin = template.Origin,
                Type = template.Type,
                Text = text,
                Messages = messages,
                Config = template.Config,
                ModelPatch = PromptConfig.ParseModelPatch(template.Config),
                Profile = PromptConfig.ParseProfile(template.Config),
            };
        }
        catch (PromptRenderException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new PromptRenderException(
                $"Prompt '{template.Name}' (version {template.Version}, {template.Origin}) could not be rendered: {ex.Message}",
                ex);
        }
    }

    public async Task<IReadOnlyList<PromptSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new Dictionary<string, PromptSummary>(StringComparer.Ordinal);
        foreach (var template in _local.All)
        {
            summaries[template.Name] = Summarize(template, externalUrl: null);
        }

        if (UsesLangfuse)
        {
            IReadOnlyList<LangfusePromptListItem>? items = null;
            try
            {
                items = await _langfuse.ListAllPromptsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.ListFailed(ex.Message);
            }

            if (items is { Count: > 0 })
            {
                var remote = new ConcurrentDictionary<string, PromptSummary>(StringComparer.Ordinal);
                var parallelism = new ParallelOptions { MaxDegreeOfParallelism = ListConcurrency, CancellationToken = cancellationToken };
                await Parallel.ForEachAsync(items, parallelism, async (item, token) =>
                    remote[item.Name] = await SummarizeLangfuseAsync(item, token).ConfigureAwait(false)).ConfigureAwait(false);

                foreach (var (name, summary) in remote)
                {
                    summaries[name] = summary;
                }
            }
        }

        return summaries.Values.OrderBy(summary => summary.Name, StringComparer.Ordinal).ToList();
    }

    public string RenderTemplate(string template, IReadOnlyDictionary<string, object?> variables)
    {
        using var activity = PromptsTelemetry.ActivitySource.StartActivity(PromptsTelemetry.RenderActivityName);
        activity?.SetTag(PromptsTelemetry.PromptNameTag, "(ad hoc)");
        return _renderer.Render(template, variables);
    }

    // Other instances pick it up when their cache expires (Prompts:CacheTtlSeconds).
    internal void Invalidate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Interlocked.Increment(ref _generation);
        _cache.RemoveWhere(key => string.Equals(key.Name, name, StringComparison.Ordinal));
        foreach (var running in _inflight.Where(pair => string.Equals(pair.Key.Name, name, StringComparison.Ordinal)).ToList())
        {
            _inflight.TryRemove(running);
        }
    }

    public void Dispose()
    {
        // DI disposes this instance twice: as PromptService and through the IPromptService factory registration.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private async Task<PromptSummary> SummarizeLangfuseAsync(LangfusePromptListItem item, CancellationToken cancellationToken)
    {
        var url = await _links.GetPromptUrlAsync(item.Name, cancellationToken).ConfigureAwait(false);

        var entry = await GetEntryAsync(CacheKey.Create(item.Name, null, _defaultLabel), cancellationToken).ConfigureAwait(false);
        if (entry is { Template: null, IsFailure: false } && _defaultLabel != LatestLabel)
        {
            entry = await GetEntryAsync(CacheKey.Create(item.Name, new PromptSelector(Label: LatestLabel), _defaultLabel), cancellationToken)
                .ConfigureAwait(false);
        }

        return entry.Template is { } template
            ? Summarize(template, url)
            : new PromptSummary(item.Name, PromptOrigin.Langfuse, item.Versions.Count > 0 ? item.Versions.Max() : 1, item.Type, item.Labels, [], url);
    }

    private PromptSummary Summarize(PromptTemplate template, string? externalUrl) => new(
        template.Name,
        template.Origin,
        template.Version,
        template.Type,
        template.Labels,
        template.ExtractVariables(_renderer),
        externalUrl);

    private async Task<CacheEntry> GetEntryAsync(CacheKey key, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(key, out var cached) && cached.FreshUntil > _time.GetUtcNow())
        {
            return cached;
        }

        // A fetch that started before an invalidation may bring the old version, so it is not kept.
        var generation = Interlocked.Read(ref _generation);
        try
        {
            var template = await FetchSharedAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
            var entry = new CacheEntry(template, _time.GetUtcNow() + _ttl, Failure: null);
            if (generation == Interlocked.Read(ref _generation))
            {
                _cache.Set(key, entry);
            }

            return entry;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var retryAt = _time.GetUtcNow() + _failureBackoff;
            if (cached is { IsFailure: false })
            {
                // Stale-on-error: keep serving the last answer from Langfuse, which may also be "not found".
                if (cached.Template is { } stale)
                {
                    _logger.ServingStalePrompt(key.Name, key.Describe(), stale.Version, ex.Message);
                }

                var extended = cached with { FreshUntil = retryAt };
                _cache.Set(key, extended);
                return extended;
            }

            _logger.PromptFetchFailed(key.Name, key.Describe(), ex.Message);
            var failure = new CacheEntry(null, retryAt, ex);
            _cache.Set(key, failure);
            return failure;
        }
    }

    private PromptTemplate Resolve(CacheKey key, CacheEntry entry)
    {
        if (entry.Template is { } template)
        {
            return template.Snapshot();
        }

        if (_options.FallbackToLocal && _local.TryGet(key.Name, out var local))
        {
            _logger.UsingLocalPrompt(key.Name);
            return local;
        }

        if (entry.Failure is { } failure)
        {
            var reason = _options.FallbackToLocal ? "it has no local default" : "the local fallback is disabled";
            throw new PromptStoreUnavailableException(
                key.Name,
                $"Prompt '{key.Name}' could not be loaded from Langfuse and {reason}: {failure.Message}",
                failure);
        }

        throw new PromptNotFoundException(key.Name);
    }

    private Task<PromptTemplate?> FetchSharedAsync(CacheKey key)
    {
        while (true)
        {
            if (_inflight.TryGetValue(key, out var running))
            {
                return running;
            }

            var completion = new TaskCompletionSource<PromptTemplate?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_inflight.TryAdd(key, completion.Task))
            {
                _ = FetchIntoAsync(key, completion);
                return completion.Task;
            }
        }
    }

    private async Task FetchIntoAsync(CacheKey key, TaskCompletionSource<PromptTemplate?> completion)
    {
        try
        {
            // Not tied to a caller's token because other callers may be waiting for the same result.
            completion.TrySetResult(await FetchAsync(key, _shutdownToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);

            // Observed here so that callers that gave up do not cause an UnobservedTaskException.
            _ = completion.Task.Exception;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<CacheKey, Task<PromptTemplate?>>(key, completion.Task));
        }
    }

    private async Task<PromptTemplate?> FetchAsync(CacheKey key, CancellationToken cancellationToken)
    {
        using var activity = PromptsTelemetry.ActivitySource.StartActivity(PromptsTelemetry.FetchActivityName, ActivityKind.Client);
        activity?.SetTag(PromptsTelemetry.PromptNameTag, key.Name);
        activity?.SetTag(PromptsTelemetry.PromptSelectorTag, key.Describe());

        try
        {
            var template = await _langfuse.GetPromptAsync(key.Name, key.Label, key.Version, cancellationToken).ConfigureAwait(false);
            activity?.SetTag(PromptsTelemetry.PromptFoundTag, template is not null);
            if (template is null)
            {
                _logger.PromptNotInLangfuse(key.Name, key.Describe());
            }
            else
            {
                activity?.SetTag(PromptsTelemetry.PromptVersionTag, template.Version);
                _logger.PromptFetched(key.Name, template.Version, key.Describe());
            }

            return template;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }

    internal readonly record struct CacheKey(string Name, string? Label, int? Version)
    {
        public static CacheKey Create(string name, PromptSelector? selector, string defaultLabel) => selector?.Version is { } version
            ? new CacheKey(name, null, version)
            : new CacheKey(name, string.IsNullOrWhiteSpace(selector?.Label) ? defaultLabel : selector.Label.Trim(), null);

        public string Describe() => Version is { } version ? $"version {version}" : $"label {Label}";
    }

    private sealed record CacheEntry(PromptTemplate? Template, DateTimeOffset FreshUntil, Exception? Failure)
    {
        public bool IsFailure => Failure is not null;
    }
}
