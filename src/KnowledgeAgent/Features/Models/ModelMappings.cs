using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Profiles;

namespace KnowledgeAgent.Features.Models;

internal static class ModelMappings
{
    public static ModelProfileDto ToDto(this ModelProfileState state) => new(
        state.Name,
        state.Kind.ToContractValue(),
        state.Configured.ToSettingsDto(),
        state.Override?.ToSettingsDto(),
        state.Effective?.ToEffectiveDto(),
        state.Error,
        state.Override?.UpdatedAt);

    public static string ToContractValue(this ModelKind kind) => kind switch
    {
        ModelKind.Embedding => "embedding",
        ModelKind.Rerank => "rerank",
        _ => "chat",
    };

    public static ModelSettingsDto ToSettingsDto(this ModelProfileOptions options) => new(
        string.IsNullOrWhiteSpace(options.Provider) ? "auto" : options.Provider,
        options.Model.TrimToNull(),
        options.Temperature,
        options.MaxOutputTokens,
        options.ReasoningEffort,
        options.ReasoningOutput,
        options.Dimensions);

    public static ModelSettingsDto ToSettingsDto(this ModelProfileOverride profileOverride) => new(
        profileOverride.Provider,
        profileOverride.Model.TrimToNull(),
        profileOverride.Temperature,
        profileOverride.MaxOutputTokens,
        profileOverride.ReasoningEffort,
        profileOverride.ReasoningOutput,
        profileOverride.Dimensions);

    public static ModelSettingsDto ToSettingsDto(this ModelProfilePatch patch) => new(
        patch.Provider ?? string.Empty,
        patch.Model,
        patch.Temperature,
        patch.MaxOutputTokens,
        patch.ReasoningEffort,
        patch.ReasoningOutput,
        Dimensions: null);

    public static EffectiveModelDto ToEffectiveDto(this ResolvedModelProfile profile) => new(
        profile.ProviderKey,
        profile.ProviderType,
        profile.Model,
        profile.Temperature,
        profile.MaxOutputTokens,
        profile.ReasoningEffort,
        profile.ReasoningOutput,
        profile.Dimensions,
        profile.Source.ToString());
}
