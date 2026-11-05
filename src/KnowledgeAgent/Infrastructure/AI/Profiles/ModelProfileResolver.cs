using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Profiles;

internal sealed partial class ModelProfileResolver(
    IAiOptionsSource optionsSource,
    AiProviderRegistry providers,
    IServiceScopeFactory scopeFactory,
    ILogger<ModelProfileResolver> logger) : IModelProfileResolver
{
    public async Task<ResolvedModelProfile> ResolveAsync(
        string profileName,
        ModelProfilePatch? promptPatch = null,
        ModelProfilePatch? requestPatch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var options = await optionsSource.GetCurrentAsync(cancellationToken);
        var (name, configured) = FindProfile(options, profileName.Trim());
        var profileOverride = await LoadOverrideAsync(name, cancellationToken);
        var profile = Resolve(options, name, configured, profileOverride, promptPatch, requestPatch);
        LogResolved(logger, profile.Name, profile.ProviderKey, profile.Model, profile.Source);
        return profile;
    }

    public async Task<IReadOnlyList<ModelProfileState>> ListAsync(CancellationToken cancellationToken = default)
    {
        var options = await optionsSource.GetCurrentAsync(cancellationToken);
        var overrides = await LoadOverridesAsync(cancellationToken);
        var states = new List<ModelProfileState>(options.Profiles.Count);
        foreach (var (name, configured) in options.Profiles)
        {
            overrides.TryGetValue(name, out var profileOverride);
            try
            {
                var effective = Resolve(options, name, configured, profileOverride, promptPatch: null, requestPatch: null);
                states.Add(new ModelProfileState(name, configured.Kind, configured, profileOverride, effective, null));
            }
            catch (ModelNotConfiguredException ex)
            {
                states.Add(new ModelProfileState(name, configured.Kind, configured, profileOverride, null, ex.Message));
            }
        }

        return states;
    }

    internal ResolvedModelProfile Resolve(
        AiOptions options,
        string name,
        ModelProfileOptions configured,
        ModelProfileOverride? profileOverride,
        ModelProfilePatch? promptPatch,
        ModelProfilePatch? requestPatch)
    {
        var settings = ProfileSettings.From(configured);
        var source = ModelProfileSource.Configuration;
        if (profileOverride is not null)
        {
            ApplyLayer(ref settings, ref source, ProfileLayer.From(profileOverride), ModelProfileSource.Override);
        }

        if (promptPatch is not null)
        {
            ApplyLayer(ref settings, ref source, ProfileLayer.From(promptPatch), ModelProfileSource.Prompt);
        }

        if (requestPatch is not null)
        {
            ApplyLayer(ref settings, ref source, ProfileLayer.From(requestPatch), ModelProfileSource.Request);
        }

        var kind = configured.Kind;
        var selection = SelectProvider(options, name, kind, settings.Provider);
        var model = settings.Model ?? DefaultModel(selection, kind) ?? throw new ModelNotConfiguredException(
            $"The '{name}' profile has no model for provider '{selection.Key}': set AI:Providers:{selection.Key}:"
            + kind switch { ModelKind.Embedding => "DefaultEmbeddingModel", ModelKind.Rerank => "DefaultRerankModel", _ => "DefaultChatModel" }
            + $" or choose a model for the '{name}' profile in Models.");

        if (settings.MaxOutputTokens is <= 0)
        {
            throw new ModelNotConfiguredException(
                $"The '{name}' profile has an invalid MaxOutputTokens value ({settings.MaxOutputTokens}): use a positive number or leave it empty.");
        }

        int? dimensions = null;
        if (kind == ModelKind.Embedding)
        {
            dimensions = settings.Dimensions
                ?? selection.Options.DefaultEmbeddingDimensions
                ?? (selection.Provider as IAiProviderDescriptor)?.GetBuiltInDefaultDimensions();
            if (dimensions is <= 0)
            {
                throw new ModelNotConfiguredException(
                    $"The '{name}' profile has invalid embedding dimensions ({dimensions}): use a positive number or leave it empty for the model default.");
            }
        }

        return new ResolvedModelProfile
        {
            Name = name,
            Kind = kind,
            ProviderKey = selection.Key,
            ProviderType = selection.Provider.Type.Trim().ToLowerInvariant(),
            Model = model,
            Temperature = settings.Temperature,
            MaxOutputTokens = settings.MaxOutputTokens,
            ReasoningEffort = settings.ReasoningEffort,
            ReasoningOutput = settings.ReasoningOutput,
            Dimensions = dimensions,
            Source = source,
        };
    }

    private static void ApplyLayer(ref ProfileSettings settings, ref ModelProfileSource source, ProfileLayer layer, ModelProfileSource layerSource)
    {
        var next = settings.Apply(layer);
        if (next != settings)
        {
            source = layerSource;
            settings = next;
        }
    }

    private ProviderSelection SelectProvider(AiOptions options, string profileName, ModelKind kind, string requestedProvider)
    {
        return ProviderTypes.IsAuto(requestedProvider)
            ? SelectAutoProvider(options, profileName, kind)
            : SelectExplicitProvider(options, profileName, kind, requestedProvider);
    }

    private ProviderSelection SelectAutoProvider(AiOptions options, string profileName, ModelKind kind)
    {
        var tried = new List<string>();
        foreach (var candidate in options.GetAutoProviders(kind))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!TryGetEntry(options.Providers, candidate.Trim(), out var key, out var providerOptions))
            {
                tried.Add($"{candidate} (not defined in AI:Providers)");
                continue;
            }

            if (providers.Find(providerOptions.Type) is not { } provider)
            {
                tried.Add($"{key} (unsupported type '{providerOptions.Type}')");
                continue;
            }

            if (!provider.Supports(kind))
            {
                tried.Add($"{key} (no {KindText(kind)} support)");
                continue;
            }

            if (!provider.IsConfigured(providerOptions))
            {
                tried.Add($"{key} (not configured: {AiProviderRegistry.DescribeMissingConfiguration(provider, key, providerOptions)})");
                continue;
            }

            return new ProviderSelection(key, providerOptions, provider);
        }

        var triedText = tried.Count == 0 ? "no providers are listed" : string.Join("; ", tried);
        throw new ModelNotConfiguredException(
            $"No configured {KindText(kind)} provider is available for the '{profileName}' profile (Provider = auto). "
            + $"Tried: {triedText}. Configure one of these providers or switch the '{profileName}' profile in Models.");
    }

    private ProviderSelection SelectExplicitProvider(AiOptions options, string profileName, ModelKind kind, string requestedProvider)
    {
        if (!TryGetEntry(options.Providers, requestedProvider, out var key, out var providerOptions))
        {
            var available = options.Providers.Count == 0 ? "none" : string.Join(", ", options.Providers.Keys);
            throw new ModelNotConfiguredException(
                $"The '{profileName}' profile uses provider '{requestedProvider}', which is not defined in AI:Providers (available: {available}). "
                + $"Add AI:Providers:{requestedProvider} or switch the '{profileName}' profile in Models.");
        }

        if (providers.Find(providerOptions.Type) is not { } provider)
        {
            throw new ModelNotConfiguredException(
                $"Provider '{key}' has type '{providerOptions.Type}', which is not supported (supported types: {providers.KnownTypes}). "
                + $"Fix AI:Providers:{key}:Type or switch the '{profileName}' profile in Models.");
        }

        if (!provider.Supports(kind))
        {
            throw new ModelNotConfiguredException(
                $"Provider '{key}' ({provider.Type}) does not support {CapabilityText(kind)}. "
                + $"Switch the '{profileName}' profile to another provider in Models.");
        }

        if (!provider.IsConfigured(providerOptions))
        {
            throw new ModelNotConfiguredException(
                $"Provider '{key}' is not configured: {AiProviderRegistry.DescribeMissingConfiguration(provider, key, providerOptions)}, "
                + $"or switch the '{profileName}' profile in Models.");
        }

        return new ProviderSelection(key, providerOptions, provider);
    }

    // A chat provider used for reranking falls back to its chat model when it names no rerank model.
    private static string? DefaultModel(ProviderSelection selection, ModelKind kind)
    {
        var descriptor = selection.Provider as IAiProviderDescriptor;
        return kind switch
        {
            ModelKind.Embedding => Clean(selection.Options.DefaultEmbeddingModel) ?? Clean(descriptor?.GetBuiltInDefaultModel(kind)),
            ModelKind.Rerank => Clean(selection.Options.DefaultRerankModel)
                ?? Clean(descriptor?.GetBuiltInDefaultModel(kind))
                ?? (selection.Provider.SupportsRerank
                    ? null
                    : Clean(selection.Options.DefaultChatModel) ?? Clean(descriptor?.GetBuiltInDefaultModel(ModelKind.Chat))),
            _ => Clean(selection.Options.DefaultChatModel) ?? Clean(descriptor?.GetBuiltInDefaultModel(kind)),
        };
    }

    private static (string Name, ModelProfileOptions Configured) FindProfile(AiOptions options, string profileName)
    {
        if (TryGetEntry(options.Profiles, profileName, out var name, out var configured))
        {
            return (name, configured);
        }

        var available = options.Profiles.Count == 0 ? "no profiles are configured" : "configured profiles: " + string.Join(", ", options.Profiles.Keys);
        throw new ModelNotConfiguredException(
            $"Model profile '{profileName}' is not configured. Add it under AI:Profiles:{profileName} ({available}).");
    }

    private async Task<ModelProfileOverride?> LoadOverrideAsync(string name, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IModelProfileOverrideStore>() is not { } store)
        {
            return null;
        }

        try
        {
            return await store.GetAsync(name, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogOverrideStoreFailed(logger, ex, name);
            return null;
        }
    }

    private async Task<Dictionary<string, ModelProfileOverride>> LoadOverridesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ModelProfileOverride>(StringComparer.OrdinalIgnoreCase);
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IModelProfileOverrideStore>() is not { } store)
        {
            return result;
        }

        try
        {
            foreach (var profileOverride in await store.ListAsync(cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(profileOverride.Name))
                {
                    result[profileOverride.Name.Trim()] = profileOverride;
                }
            }
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            LogOverrideStoreFailed(logger, ex, "*");
        }

        return result;
    }

    private static bool TryGetEntry<T>(Dictionary<string, T> dictionary, string key, out string canonicalKey, out T value)
    {
        foreach (var (candidate, candidateValue) in dictionary)
        {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase) && candidateValue is not null)
            {
                canonicalKey = candidate;
                value = candidateValue;
                return true;
            }
        }

        canonicalKey = key;
        value = default!;
        return false;
    }

    private static string CapabilityText(ModelKind kind) => kind switch
    {
        ModelKind.Embedding => "embeddings",
        ModelKind.Rerank => "reranking",
        _ => "chat",
    };

    private static string KindText(ModelKind kind) => kind switch
    {
        ModelKind.Embedding => "embedding",
        ModelKind.Rerank => "rerank",
        _ => "chat",
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Model profile {Profile} resolved to {Provider}/{Model} (source {Source})")]
    private static partial void LogResolved(ILogger logger, string profile, string provider, string model, ModelProfileSource source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load model profile override(s) for {Profile}; using configuration instead")]
    private static partial void LogOverrideStoreFailed(ILogger logger, Exception exception, string profile);

    private sealed record ProviderSelection(string Key, AiProviderOptions Options, IAiProvider Provider);

    private sealed record ProfileLayer(
        string? Provider,
        string? Model,
        float? Temperature,
        int? MaxOutputTokens,
        string? ReasoningEffort,
        string? ReasoningOutput,
        int? Dimensions)
    {
        public static ProfileLayer From(ModelProfileOverride o) =>
            new(o.Provider, o.Model, o.Temperature, o.MaxOutputTokens, o.ReasoningEffort, o.ReasoningOutput, o.Dimensions);

        public static ProfileLayer From(ModelProfilePatch p) =>
            new(p.Provider, p.Model, p.Temperature, p.MaxOutputTokens, p.ReasoningEffort, p.ReasoningOutput, Dimensions: null);
    }

    // Value equality tells ApplyLayer whether a layer changed anything.
    private sealed record ProfileSettings(
        string Provider,
        string? Model,
        float? Temperature,
        int? MaxOutputTokens,
        string? ReasoningEffort,
        string? ReasoningOutput,
        int? Dimensions)
    {
        public static ProfileSettings From(ModelProfileOptions configured) => new(
            Clean(configured.Provider) ?? ProviderTypes.Auto,
            Clean(configured.Model),
            configured.Temperature,
            configured.MaxOutputTokens,
            ReasoningSettings.NormalizeEffort(configured.ReasoningEffort),
            ReasoningSettings.NormalizeOutput(configured.ReasoningOutput),
            configured.Dimensions);

        public ProfileSettings Apply(ProfileLayer layer)
        {
            var provider = Provider;
            var model = Model;
            var dimensions = Dimensions;
            if (Clean(layer.Provider) is { } layerProvider && !string.Equals(layerProvider, Provider, StringComparison.OrdinalIgnoreCase))
            {
                // A model id or dimension size from another provider is meaningless, so they fall back to the new defaults.
                provider = layerProvider;
                model = null;
                dimensions = null;
            }

            return new ProfileSettings(
                provider,
                Clean(layer.Model) ?? model,
                layer.Temperature ?? Temperature,
                layer.MaxOutputTokens ?? MaxOutputTokens,
                ReasoningSettings.NormalizeEffort(layer.ReasoningEffort) ?? ReasoningEffort,
                ReasoningSettings.NormalizeOutput(layer.ReasoningOutput) ?? ReasoningOutput,
                layer.Dimensions ?? dimensions);
        }
    }
}
