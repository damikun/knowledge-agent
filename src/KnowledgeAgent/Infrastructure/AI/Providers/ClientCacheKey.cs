using System.Security.Cryptography;
using System.Text;

namespace KnowledgeAgent.Infrastructure.AI.Providers;

// The API key and the settings are hashed so that secrets never end up in the cache key.
internal static class ClientCacheKey
{
    public static string Create(string providerKey, string providerType, string model, AiProviderOptions options, params string?[] extra)
    {
        var settings = string.Join(
            ";",
            options.Settings
                .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => $"{s.Key.ToLowerInvariant()}={s.Value}"));

        return string.Join(
            "|",
            providerKey.ToLowerInvariant(),
            providerType.ToLowerInvariant(),
            model,
            options.Endpoint?.Trim() ?? string.Empty,
            Hash(options.ApiKey),
            Hash(settings),
            string.Join(",", extra.Select(e => e ?? string.Empty)));
    }

    private static string Hash(string? value) =>
        string.IsNullOrEmpty(value)
            ? "-"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 8);
}
