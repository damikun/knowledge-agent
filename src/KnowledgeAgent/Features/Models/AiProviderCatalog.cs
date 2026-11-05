using KnowledgeAgent.Infrastructure.AI;

namespace KnowledgeAgent.Features.Models;

internal static class AiProviderCatalog
{
    public static KeyValuePair<string, AiProviderOptions>? FindProvider(AiOptions options, string key)
    {
        foreach (var entry in options.Providers)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    public static KeyValuePair<string, ModelProfileOptions>? FindProfile(AiOptions options, string name)
    {
        foreach (var entry in options.Profiles)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}
