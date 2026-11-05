using Microsoft.Extensions.Configuration;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal static class AiOptionsPostConfigure
{
    public static void Apply(AiOptions options, IConfiguration configuration)
    {
        options.Providers = CaseInsensitive(options.Providers);
        options.Profiles = CaseInsensitive(options.Profiles);

        foreach (var key in options.Providers.Keys.ToList())
        {
            if (options.Providers[key] is not { } provider)
            {
                options.Providers.Remove(key);
                continue;
            }

            provider.Type = string.IsNullOrWhiteSpace(provider.Type) ? key.Trim() : provider.Type.Trim();
            provider.Settings = CaseInsensitive(provider.Settings);

            if (provider.Type.Equals(ProviderTypes.Anthropic, StringComparison.OrdinalIgnoreCase))
            {
                ApplyApiKeyFallback(key, provider, configuration, FallbackConfigurationKeys.AnthropicApiKey);
            }
            else if (provider.Type.Equals(ProviderTypes.OpenAI, StringComparison.OrdinalIgnoreCase))
            {
                ApplyApiKeyFallback(key, provider, configuration, FallbackConfigurationKeys.OpenAIApiKey);
            }
            else if (provider.Type.Equals(ProviderTypes.Voyage, StringComparison.OrdinalIgnoreCase))
            {
                ApplyApiKeyFallback(key, provider, configuration, FallbackConfigurationKeys.VoyageApiKey);
            }
            else if (provider.Type.Equals(ProviderTypes.Ollama, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(provider.Endpoint)
                && NonBlank(configuration[FallbackConfigurationKeys.OllamaEndpoint]) is { } endpoint)
            {
                provider.Endpoint = endpoint;
            }
        }

        foreach (var key in options.Profiles.Keys.ToList())
        {
            if (options.Profiles[key] is null)
            {
                options.Profiles.Remove(key);
            }
        }
    }

    // Keeps a vendor's API key from being sent to an unrelated compatible server configured under another key.
    public static bool AllowsApiKeyFallback(string providerKey, AiProviderOptions provider) =>
        string.Equals(providerKey.Trim(), provider.Type.Trim(), StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(provider.Endpoint);

    private static void ApplyApiKeyFallback(string key, AiProviderOptions provider, IConfiguration configuration, string fallbackKey)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey)
            && AllowsApiKeyFallback(key, provider)
            && NonBlank(configuration[fallbackKey]) is { } apiKey)
        {
            provider.ApiKey = apiKey;
        }
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, T> CaseInsensitive<T>(Dictionary<string, T>? dictionary)
    {
        if (dictionary is not null && ReferenceEquals(dictionary.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            return dictionary;
        }

        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        if (dictionary is not null)
        {
            foreach (var (key, value) in dictionary)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
