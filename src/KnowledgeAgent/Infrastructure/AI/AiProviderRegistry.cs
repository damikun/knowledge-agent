namespace KnowledgeAgent.Infrastructure.AI;

internal interface IAiProviderDescriptor
{
    string DescribeMissingConfiguration(string providerKey, AiProviderOptions options);

    string? GetBuiltInDefaultModel(ModelKind kind) => null;

    int? GetBuiltInDefaultDimensions() => null;
}

internal sealed class AiProviderRegistry(IEnumerable<IAiProvider> providers)
{
    private readonly IAiProvider[] _providers = providers.ToArray();

    public IReadOnlyList<IAiProvider> All => _providers;

    public IAiProvider? Find(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        // Searched from the end so the last registration wins and hosts can replace the built-in providers.
        var trimmed = type.Trim();
        for (var i = _providers.Length - 1; i >= 0; i--)
        {
            if (string.Equals(_providers[i].Type, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return _providers[i];
            }
        }

        return null;
    }

    public string KnownTypes => string.Join(", ", _providers.Select(p => p.Type).Distinct(StringComparer.OrdinalIgnoreCase));

    public static string DescribeMissingConfiguration(IAiProvider provider, string providerKey, AiProviderOptions options) =>
        provider is IAiProviderDescriptor descriptor
            ? descriptor.DescribeMissingConfiguration(providerKey, options)
            : $"check the settings under AI:Providers:{providerKey}";
}
