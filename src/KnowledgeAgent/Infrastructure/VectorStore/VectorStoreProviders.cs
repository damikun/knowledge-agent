namespace KnowledgeAgent.Infrastructure.VectorStore;

internal static class VectorStoreProviders
{
    public const string ClickHouse = "ClickHouse";
    public const string InMemory = "InMemory";

    public static IReadOnlyList<string> All { get; } = [ClickHouse, InMemory];

    public static string? Normalize(string? provider)
    {
        var trimmed = provider?.Trim();
        return All.FirstOrDefault(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static string UnsupportedMessage(string? provider) =>
        $"VectorStore:Provider '{provider}' is not supported. Supported providers: {string.Join(", ", All)}.";
}
