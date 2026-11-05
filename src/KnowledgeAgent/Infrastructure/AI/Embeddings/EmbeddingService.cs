using System.Collections.Concurrent;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.AI.Providers;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Embeddings;

internal sealed partial class EmbeddingService(
    IModelProfileResolver resolver,
    AiProviderRegistry providers,
    IAiOptionsSource optionsSource,
    ILoggerFactory loggerFactory,
    IProviderErrorLog? providerErrors = null) : IEmbeddingService, IDisposable
{
    public const string ProfileName = "embedding";
    public const int BatchSize = 64;

    private const string QueryPrefix = "search_query: ";
    private const string DocumentPrefix = "search_document: ";

    private readonly ConcurrentDictionary<string, Lazy<IEmbeddingGenerator<string, Embedding<float>>>> _generators = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dimensionWarnings = new(StringComparer.Ordinal);
    private readonly ILogger _logger = loggerFactory.CreateLogger<EmbeddingService>();

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await resolver.ResolveAsync(ProfileName, cancellationToken: cancellationToken);
            return profile.Kind == ModelKind.Embedding;
        }
        catch (ModelNotConfiguredException)
        {
            return false;
        }
    }

    public async Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs,
        EmbeddingPurpose purpose = EmbeddingPurpose.Document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var profile = await resolver.ResolveAsync(ProfileName, cancellationToken: cancellationToken);
        if (profile.Kind != ModelKind.Embedding)
        {
            throw new ModelNotConfiguredException(
                $"The '{ProfileName}' profile must have Kind = Embedding (AI:Profiles:{ProfileName}:Kind).");
        }

        if (inputs.Count == 0)
        {
            var expected = profile.Dimensions ?? 0;
            return new EmbeddingResult(ModelKey(profile, expected), expected, []);
        }

        var generator = GetGenerator(profile, await optionsSource.GetCurrentAsync(cancellationToken), out var provider, out var providerOptions);
        var prefix = UsesTaskPrefixes(profile.Model) ? (purpose == EmbeddingPurpose.Query ? QueryPrefix : DocumentPrefix) : null;
        var generationOptions = new EmbeddingGenerationOptions { ModelId = profile.Model, Dimensions = profile.Dimensions };
        provider.ConfigureEmbeddingOptions(generationOptions, providerOptions, profile, purpose);

        var vectors = new List<ReadOnlyMemory<float>>(inputs.Count);
        int? dimensions = null;
        for (var offset = 0; offset < inputs.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(BatchSize, inputs.Count - offset);
            var batch = new string[count];
            for (var i = 0; i < count; i++)
            {
                var text = inputs[offset + i] ?? string.Empty;
                batch[i] = prefix is null ? text : prefix + text;
            }

            var embeddings = await generator.GenerateAsync(batch, generationOptions, cancellationToken);
            if (embeddings.Count != count)
            {
                throw new InvalidOperationException(
                    $"Embedding provider '{profile.ProviderKey}' ({profile.Model}) returned {embeddings.Count} vectors for {count} inputs.");
            }

            foreach (var embedding in embeddings)
            {
                var length = embedding.Vector.Length;
                if (length == 0)
                {
                    throw new InvalidOperationException($"Embedding provider '{profile.ProviderKey}' ({profile.Model}) returned an empty vector.");
                }

                dimensions ??= length;
                if (length != dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding provider '{profile.ProviderKey}' ({profile.Model}) returned vectors with different dimensions ({dimensions} and {length}).");
                }

                vectors.Add(embedding.Vector);
            }
        }

        var actual = dimensions!.Value;
        if (profile.Dimensions is { } configured && configured != actual && _dimensionWarnings.TryAdd($"{profile.ProviderKey}|{profile.Model}|{configured}", 0))
        {
            LogDimensionMismatch(_logger, profile.ProviderKey, profile.Model, configured, actual);
        }

        return new EmbeddingResult(ModelKey(profile, actual), actual, vectors);
    }

    internal static string ModelKey(ResolvedModelProfile profile, int dimensions) => $"{profile.ProviderType}:{profile.Model}:{dimensions}";

    // nomic-embed models are trained with "search_query: " and "search_document: " task prefixes.
    internal static bool UsesTaskPrefixes(string model) => model.Contains("nomic", StringComparison.OrdinalIgnoreCase);

    private IEmbeddingGenerator<string, Embedding<float>> GetGenerator(
        ResolvedModelProfile profile,
        AiOptions options,
        out IAiProvider provider,
        out AiProviderOptions providerOptions)
    {
        if (!options.Providers.TryGetValue(profile.ProviderKey, out providerOptions!) || providers.Find(providerOptions.Type) is not { } found)
        {
            throw new ModelNotConfiguredException(
                $"Provider '{profile.ProviderKey}' of the '{profile.Name}' profile is no longer available; check AI:Providers.");
        }

        provider = found;
        var selected = providerOptions;
        var sensitiveData = options.Telemetry.EnableSensitiveData;
        var key = ClientCacheKey.Create(
            profile.ProviderKey,
            found.Type,
            profile.Model,
            providerOptions,
            profile.Dimensions?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sensitiveData ? "sensitive" : "redacted");

        var lazy = _generators.GetOrAdd(key, _ => new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(() => Build(found, selected, profile, sensitiveData)));
        try
        {
            return lazy.Value;
        }
        catch
        {
            _generators.TryRemove(new KeyValuePair<string, Lazy<IEmbeddingGenerator<string, Embedding<float>>>>(key, lazy));
            throw;
        }
    }

    private IEmbeddingGenerator<string, Embedding<float>> Build(IAiProvider provider, AiProviderOptions providerOptions, ResolvedModelProfile profile, bool sensitiveData)
    {
        IEmbeddingGenerator<string, Embedding<float>> inner;
        try
        {
            inner = provider.CreateEmbeddingGenerator(providerOptions, profile);
        }
        catch (Exception ex) when (ex is not ModelNotConfiguredException and not OperationCanceledException)
        {
            LogCreateFailed(_logger, ex, profile.ProviderKey, profile.Model);
            throw new ModelNotConfiguredException(
                $"Could not create an embedding generator for provider '{profile.ProviderKey}' ({profile.Model}): {ex.Message} "
                + $"Check AI:Providers:{profile.ProviderKey} or switch the '{profile.Name}' profile in Models.");
        }

        LogGeneratorCreated(_logger, profile.ProviderKey, provider.Type, profile.Model, profile.Dimensions);
        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(inner)
            .UseOpenTelemetry(loggerFactory, KnowledgeAgentTelemetry.AI, generator => generator.EnableSensitiveData = sensitiveData)
            .UseLogging(loggerFactory)
            .Use(innerGenerator => providerErrors is null
                ? innerGenerator
                : new ProviderErrorEmbeddingGenerator(innerGenerator, providerErrors, profile.ProviderKey))
            .Build();
    }

    public void Dispose()
    {
        foreach (var lazy in _generators.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        _generators.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created embedding generator for provider {Provider} ({ProviderType}), model {Model}, dimensions {Dimensions}")]
    private static partial void LogGeneratorCreated(ILogger logger, string provider, string providerType, string model, int? dimensions);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating the embedding generator for provider {Provider}, model {Model} failed")]
    private static partial void LogCreateFailed(ILogger logger, Exception exception, string provider, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Embedding model {Provider}/{Model} returned {Actual} dimensions although {Configured} were configured; the returned size is used")]
    private static partial void LogDimensionMismatch(ILogger logger, string provider, string model, int configured, int actual);
}
