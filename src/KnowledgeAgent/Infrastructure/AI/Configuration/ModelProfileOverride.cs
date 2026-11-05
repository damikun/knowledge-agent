namespace KnowledgeAgent.Infrastructure.AI.Configuration;

public sealed class ModelProfileOverride
{
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "auto";
    public string? Model { get; set; }
    public float? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? ReasoningEffort { get; set; }
    public string? ReasoningOutput { get; set; }
    public int? Dimensions { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
