using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Models;

internal static class ModelEndpoints
{
    public static readonly IReadOnlyList<string> ReasoningEfforts = ["low", "medium", "high", "extra_high"];
    public static readonly IReadOnlyList<string> ReasoningOutputs = ["none", "summary", "full"];

    public static RouteGroupBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/models").WithTags("Models");

        group.MapGet("/profiles", ListProfilesAsync)
            .WithName("ListModelProfiles")
            .WithSummary("Model profiles with their configured settings, runtime override and effective model.");

        group.MapPut("/profiles/{name}", UpdateProfileAsync)
            .WithName("UpdateModelProfile")
            .WithSummary("Store a runtime override for a profile (provider key or \"auto\", model, reasoning...).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/profiles/{name}/override", DeleteOverrideAsync)
            .WithName("DeleteModelProfileOverride")
            .WithSummary("Remove the runtime override; the profile falls back to configuration.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/providers", ListProvidersAsync)
            .WithName("ListProviders")
            .WithSummary("Providers, whether they are usable, and where their API key comes from (the key itself is never returned).");

        group.MapPut("/providers/{key}", UpdateProviderAsync)
            .WithName("UpdateProvider")
            .WithSummary("Save an API key (stored encrypted), endpoint or default models for a provider.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/providers/{key}/override", ResetProviderAsync)
            .WithName("ResetProvider")
            .WithSummary("Remove the settings saved in the portal; the configured values apply again.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/providers/{key}/test", TestProviderAsync)
            .WithName("TestProvider")
            .WithSummary("Check the provider's credentials and connectivity (lists models, no tokens are generated).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Ok<ModelProfileDto[]>> ListProfilesAsync(IModelProfileResolver resolver, CancellationToken cancellationToken)
    {
        var states = await resolver.ListAsync(cancellationToken);
        return TypedResults.Ok(states.Select(s => s.ToDto()).ToArray());
    }

    private static async Task<Results<Ok<ModelProfileDto>, ValidationProblem, ProblemHttpResult>> UpdateProfileAsync(
        string name,
        UpdateModelProfileRequest request,
        IOptionsMonitor<AiOptions> aiOptions,
        AiProviderRegistry providers,
        IModelProfileOverrideStore overrides,
        IModelProfileResolver resolver,
        ModelSettingsNotifier notifier,
        CancellationToken cancellationToken)
    {
        var options = aiOptions.CurrentValue;
        if (AiProviderCatalog.FindProfile(options, name) is not { } profile)
        {
            return ProfileNotFound(name);
        }

        var errors = new ValidationErrors();
        var providerKey = ValidateProvider(request.Provider, profile.Value.Kind, options, providers, errors);
        var reasoningEffort = ValidateChoice(request.ReasoningEffort, ReasoningEfforts, "reasoningEffort", errors);
        var reasoningOutput = ValidateChoice(request.ReasoningOutput, ReasoningOutputs, "reasoningOutput", errors);

        if (request.Temperature is { } temperature && (float.IsNaN(temperature) || temperature < 0 || temperature > 2))
        {
            errors.Add("temperature", "temperature must be between 0 and 2.");
        }

        if (request.MaxOutputTokens is < 1)
        {
            errors.Add("maxOutputTokens", "maxOutputTokens must be greater than 0.");
        }

        if (request.Dimensions is not null)
        {
            if (profile.Value.Kind != ModelKind.Embedding)
            {
                errors.Add("dimensions", "dimensions only applies to embedding profiles.");
            }
            else if (request.Dimensions < 1)
            {
                errors.Add("dimensions", "dimensions must be greater than 0.");
            }
        }

        if (errors.HasErrors || providerKey is null)
        {
            return errors.ToResult();
        }

        await overrides.UpsertAsync(
            new ModelProfileOverride
            {
                Name = profile.Key,
                Provider = providerKey,
                Model = request.Model.TrimToNull(),
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxOutputTokens,
                ReasoningEffort = reasoningEffort,
                ReasoningOutput = reasoningOutput,
                Dimensions = request.Dimensions,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken);
        await notifier.NotifyAsync(cancellationToken);

        return await CurrentStateAsync(resolver, profile.Key, cancellationToken) is { } updated
            ? TypedResults.Ok(updated)
            : ProfileNotFound(profile.Key);
    }

    private static async Task<Results<Ok<ModelProfileDto>, ProblemHttpResult>> DeleteOverrideAsync(
        string name,
        IOptionsMonitor<AiOptions> aiOptions,
        IModelProfileOverrideStore overrides,
        IModelProfileResolver resolver,
        ModelSettingsNotifier notifier,
        CancellationToken cancellationToken)
    {
        if (AiProviderCatalog.FindProfile(aiOptions.CurrentValue, name) is not { } profile)
        {
            return ProfileNotFound(name);
        }

        await overrides.DeleteAsync(profile.Key, cancellationToken);
        await notifier.NotifyAsync(cancellationToken);
        return await CurrentStateAsync(resolver, profile.Key, cancellationToken) is { } restored
            ? TypedResults.Ok(restored)
            : ProfileNotFound(profile.Key);
    }

    private static async Task<Ok<ProviderDto[]>> ListProvidersAsync(IProviderSettingsService providerSettings, CancellationToken cancellationToken) =>
        TypedResults.Ok((await providerSettings.ListAsync(cancellationToken)).ToArray());

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> UpdateProviderAsync(
        string key,
        UpdateProviderRequest request,
        IProviderSettingsService providerSettings,
        CancellationToken cancellationToken) =>
        await providerSettings.UpdateAsync(key, request, cancellationToken) is { } updated
            ? TypedResults.Ok(updated)
            : ProviderNotFound(key);

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> ResetProviderAsync(
        string key,
        IProviderSettingsService providerSettings,
        CancellationToken cancellationToken) =>
        await providerSettings.ResetAsync(key, cancellationToken) is { } restored
            ? TypedResults.Ok(restored)
            : ProviderNotFound(key);

    private static async Task<Results<Ok<ProviderTestResultDto>, ProblemHttpResult>> TestProviderAsync(
        string key,
        IProviderSettingsService providerSettings,
        CancellationToken cancellationToken) =>
        await providerSettings.TestAsync(key, cancellationToken) is { } result
            ? TypedResults.Ok(result)
            : ProviderNotFound(key);

    private static string? ValidateProvider(
        string? requested,
        ModelKind kind,
        AiOptions options,
        AiProviderRegistry providers,
        ValidationErrors errors)
    {
        var key = requested?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            errors.Add("provider", $"A provider is required: \"{ProviderTypes.Auto}\" or one of {KnownProviders(options)}.");
            return null;
        }

        if (string.Equals(key, ProviderTypes.Auto, StringComparison.OrdinalIgnoreCase))
        {
            return ProviderTypes.Auto;
        }

        if (AiProviderCatalog.FindProvider(options, key) is not { } provider)
        {
            errors.Add("provider", $"Unknown provider '{key}'. Use \"{ProviderTypes.Auto}\" or one of {KnownProviders(options)}.");
            return null;
        }

        var implementation = providers.Find(provider.Value.Type);
        if (implementation is null)
        {
            errors.Add("provider", $"Provider '{provider.Key}' has type '{provider.Value.Type}', which is not available in this build.");
            return null;
        }

        if (!implementation.Supports(kind))
        {
            errors.Add(
                "provider",
                $"Provider '{provider.Key}' ({implementation.Type}) does not support {kind.ToContractValue()} models.");
            return null;
        }

        return provider.Key;
    }

    private static string? ValidateChoice(string? value, IReadOnlyList<string> allowed, string field, ValidationErrors errors)
    {
        var normalized = value.TrimToNull()?.ToLowerInvariant();
        if (normalized is not null && !allowed.Contains(normalized))
        {
            errors.Add(field, $"{field} must be one of: {string.Join(", ", allowed)}.");
            return null;
        }

        return normalized;
    }

    private static async Task<ModelProfileDto?> CurrentStateAsync(
        IModelProfileResolver resolver,
        string profileName,
        CancellationToken cancellationToken)
    {
        var states = await resolver.ListAsync(cancellationToken);
        return states.FirstOrDefault(s => string.Equals(s.Name, profileName, StringComparison.OrdinalIgnoreCase))?.ToDto();
    }

    private static string KnownProviders(AiOptions options) =>
        options.Providers.Count == 0
            ? "(none configured)"
            : string.Join(", ", options.Providers.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(k => $"'{k}'"));

    private static ProblemHttpResult ProfileNotFound(string name) =>
        ApiResults.NotFound($"Model profile '{name}' is not configured (AI:Profiles).");

    private static ProblemHttpResult ProviderNotFound(string key) =>
        ApiResults.NotFound($"Provider '{key}' is not configured (AI:Providers).");
}
