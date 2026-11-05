namespace KnowledgeAgent.Infrastructure.AI;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public static readonly IReadOnlyList<string> DefaultAutoChatProviders = ["anthropic", "openai", "ollama"];
    public static readonly IReadOnlyList<string> DefaultAutoEmbeddingProviders = ["openai", "voyage", "ollama"];

    // Dedicated rerankers only: reranking with a chat model costs one LLM call per search, so it is opt-in.
    public static readonly IReadOnlyList<string> DefaultAutoRerankProviders = ["voyage"];

    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ModelProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string>? AutoChatProviders { get; set; }
    public List<string>? AutoEmbeddingProviders { get; set; }
    public List<string>? AutoRerankProviders { get; set; }

    public AiTelemetryOptions Telemetry { get; set; } = new();

    public IReadOnlyList<string> GetAutoProviders(ModelKind kind) => kind switch
    {
        ModelKind.Embedding => AutoEmbeddingProviders is { Count: > 0 } e ? e : DefaultAutoEmbeddingProviders,
        ModelKind.Rerank => AutoRerankProviders is { Count: > 0 } r ? r : DefaultAutoRerankProviders,
        _ => AutoChatProviders is { Count: > 0 } c ? c : DefaultAutoChatProviders,
    };
}

public sealed class AiProviderOptions
{
    public string Type { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? DefaultChatModel { get; set; }
    public string? DefaultEmbeddingModel { get; set; }
    public int? DefaultEmbeddingDimensions { get; set; }
    public string? DefaultRerankModel { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum ModelKind
{
    Chat,
    Embedding,
    Rerank,
}

public sealed class ModelProfileOptions
{
    public string Provider { get; set; } = "auto";
    public string? Model { get; set; }
    public ModelKind Kind { get; set; } = ModelKind.Chat;
    public float? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string? ReasoningEffort { get; set; }
    public string? ReasoningOutput { get; set; }
    public int? Dimensions { get; set; }
}

public sealed class AiTelemetryOptions
{
    public bool EnableSensitiveData { get; set; } = true;
}
