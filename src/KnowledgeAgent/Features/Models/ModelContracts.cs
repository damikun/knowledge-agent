namespace KnowledgeAgent.Features.Models;

public sealed record ModelSettingsDto(
    string Provider,
    string? Model,
    float? Temperature,
    int? MaxOutputTokens,
    string? ReasoningEffort,
    string? ReasoningOutput,
    int? Dimensions);

public sealed record EffectiveModelDto(
    string ProviderKey,
    string ProviderType,
    string Model,
    float? Temperature,
    int? MaxOutputTokens,
    string? ReasoningEffort,
    string? ReasoningOutput,
    int? Dimensions,
    string Source);

public sealed record ModelProfileDto(
    string Name,
    string Kind,
    ModelSettingsDto Configured,
    ModelSettingsDto? Override,
    EffectiveModelDto? Effective,
    string? Error,
    DateTimeOffset? OverrideUpdatedAt);

public sealed record UpdateModelProfileRequest(
    string Provider,
    string? Model = null,
    float? Temperature = null,
    int? MaxOutputTokens = null,
    string? ReasoningEffort = null,
    string? ReasoningOutput = null,
    int? Dimensions = null);

public sealed record ProviderDto(
    string Key,
    string Type,
    bool Configured,
    bool SupportsChat,
    bool SupportsEmbeddings,
    string? DefaultChatModel,
    string? DefaultEmbeddingModel,
    int? DefaultEmbeddingDimensions = null,
    string? Endpoint = null,
    bool UsesApiKey = false,
    bool UsesEndpoint = false,
    bool EndpointRequired = false,
    string ApiKeySource = "none",
    string? ApiKeyHint = null,
    bool HasOverride = false,
    DateTimeOffset? OverrideUpdatedAt = null,
    bool SupportsRerank = false,
    string? DefaultRerankModel = null);

public sealed record UpdateProviderRequest(
    string? ApiKey = null,
    bool ClearApiKey = false,
    string? Endpoint = null,
    string? DefaultChatModel = null,
    string? DefaultEmbeddingModel = null,
    int? DefaultEmbeddingDimensions = null);

public sealed record ProviderTestResultDto(string Key, bool Ok, string Message, IReadOnlyList<string> Models, long DurationMs);
