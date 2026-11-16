namespace KnowledgeAgent.Infrastructure.Persistence;

internal static class LikeSearch
{
    public const string EscapeCharacter = "\\";
    private const int MaxTerms = 8;
    private const int MaxTermLength = 100;

    public static IReadOnlyList<string> Terms(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(term => term.Length > MaxTermLength ? term[..MaxTermLength] : term)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxTerms)
                .ToList();

    public static string Escape(string value) =>
        value.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
