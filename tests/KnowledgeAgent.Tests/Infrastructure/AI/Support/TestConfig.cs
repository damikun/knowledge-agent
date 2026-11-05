namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal static class TestConfig
{
    public static Dictionary<string, string?> Base() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["AI:Providers:anthropic:Type"] = "anthropic",
        ["AI:Providers:anthropic:ApiKey"] = "",
        ["AI:Providers:anthropic:DefaultChatModel"] = "claude-opus-5-5",
        ["AI:Providers:anthropic:Settings:RefusalFallback"] = "default",
        ["AI:Providers:openai:Type"] = "openai",
        ["AI:Providers:openai:ApiKey"] = "",
        ["AI:Providers:openai:DefaultChatModel"] = "gpt-5-mini",
        ["AI:Providers:openai:DefaultEmbeddingModel"] = "text-embedding-3-small",
        ["AI:Providers:openai:DefaultEmbeddingDimensions"] = "1536",
        ["AI:Providers:ollama:Type"] = "ollama",
        ["AI:Providers:ollama:Endpoint"] = "",
        ["AI:Providers:ollama:DefaultChatModel"] = "qwen3",
        ["AI:Providers:ollama:DefaultEmbeddingModel"] = "nomic-embed-text",
        ["AI:Providers:voyage:Type"] = "voyage",
        ["AI:Providers:voyage:ApiKey"] = "",
        ["AI:Providers:voyage:DefaultEmbeddingModel"] = "voyage-4",
        ["AI:Providers:voyage:DefaultEmbeddingDimensions"] = "1024",
        ["AI:Providers:voyage:DefaultRerankModel"] = "rerank-2.5",
        ["AI:Profiles:orchestrator:Provider"] = "auto",
        ["AI:Profiles:orchestrator:Kind"] = "Chat",
        ["AI:Profiles:orchestrator:ReasoningEffort"] = "medium",
        ["AI:Profiles:orchestrator:ReasoningOutput"] = "summary",
        ["AI:Profiles:orchestrator:MaxOutputTokens"] = "16000",
        ["AI:Profiles:researcher:Provider"] = "auto",
        ["AI:Profiles:researcher:Kind"] = "Chat",
        ["AI:Profiles:researcher:ReasoningEffort"] = "medium",
        ["AI:Profiles:researcher:MaxOutputTokens"] = "16000",
        ["AI:Profiles:utility:Provider"] = "auto",
        ["AI:Profiles:utility:Kind"] = "Chat",
        ["AI:Profiles:utility:ReasoningEffort"] = "low",
        ["AI:Profiles:utility:MaxOutputTokens"] = "2000",
        ["AI:Profiles:embedding:Provider"] = "auto",
        ["AI:Profiles:embedding:Kind"] = "Embedding",
    };

    public static Dictionary<string, string?> WithFakeProviders() => Base().With(
        ("AI:Providers:fake:Type", "fake"),
        ("AI:Providers:fake:DefaultChatModel", "fake-chat"),
        ("AI:Providers:fake:DefaultEmbeddingModel", "fake-embed"),
        ("AI:Providers:fake2:Type", "fake"),
        ("AI:Providers:fake2:DefaultChatModel", "fake2-chat"),
        ("AI:Providers:fake2:DefaultEmbeddingModel", "fake2-embed"));

    public static Dictionary<string, string?> With(this Dictionary<string, string?> config, params (string Key, string? Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            config[key] = value;
        }

        return config;
    }

    public static Dictionary<string, string?> Without(this Dictionary<string, string?> config, string prefix)
    {
        foreach (var key in config.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            config.Remove(key);
        }

        return config;
    }
}
