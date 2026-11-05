namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public sealed class ProviderSettingsOverride
{
    public string Key { get; set; } = string.Empty;
    public string? ProtectedApiKey { get; set; }
    public string? ApiKeyHint { get; set; }
    public string? Endpoint { get; set; }
    public string? DefaultChatModel { get; set; }
    public string? DefaultEmbeddingModel { get; set; }
    public int? DefaultEmbeddingDimensions { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
