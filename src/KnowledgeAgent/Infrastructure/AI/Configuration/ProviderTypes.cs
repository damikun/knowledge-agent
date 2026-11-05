namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal static class ProviderTypes
{
    public const string Anthropic = "anthropic";
    public const string OpenAI = "openai";
    public const string Ollama = "ollama";
    public const string Voyage = "voyage";
    public const string Auto = "auto";

    public static bool IsAuto(string? provider) => string.Equals(provider?.Trim(), Auto, StringComparison.OrdinalIgnoreCase);
}

internal static class FallbackConfigurationKeys
{
    public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
    public const string OpenAIApiKey = "OPENAI_API_KEY";
    public const string OllamaEndpoint = "OLLAMA_ENDPOINT";
    public const string VoyageApiKey = "VOYAGE_API_KEY";
}
