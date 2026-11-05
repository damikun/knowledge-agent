using KnowledgeAgent.Infrastructure.AI.Configuration;

namespace KnowledgeAgent.Infrastructure.AI.Profiles;

public enum ModelProfileSource
{
    Configuration,
    Override,
    Prompt,
    Request,
}

public sealed record ModelProfilePatch(
    string? Provider = null,
    string? Model = null,
    float? Temperature = null,
    int? MaxOutputTokens = null,
    string? ReasoningEffort = null,
    string? ReasoningOutput = null)
{
    public bool IsEmpty => Provider is null && Model is null && Temperature is null && MaxOutputTokens is null
        && ReasoningEffort is null && ReasoningOutput is null;
}

public sealed record ResolvedModelProfile
{
    public required string Name { get; init; }
    public required ModelKind Kind { get; init; }
    public required string ProviderKey { get; init; }
    public required string ProviderType { get; init; }
    public required string Model { get; init; }
    public float? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? ReasoningEffort { get; init; }
    public string? ReasoningOutput { get; init; }
    public int? Dimensions { get; init; }
    public required ModelProfileSource Source { get; init; }
}

public sealed record ModelProfileState(
    string Name,
    ModelKind Kind,
    ModelProfileOptions Configured,
    ModelProfileOverride? Override,
    ResolvedModelProfile? Effective,
    string? Error);

public interface IModelProfileResolver
{
    /// <summary>Precedence: request patch, then prompt patch, then database override, then configuration.</summary>
    Task<ResolvedModelProfile> ResolveAsync(
        string profileName,
        ModelProfilePatch? promptPatch = null,
        ModelProfilePatch? requestPatch = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelProfileState>> ListAsync(CancellationToken cancellationToken = default);
}
