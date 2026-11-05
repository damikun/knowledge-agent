using System.Text.RegularExpressions;

namespace KnowledgeAgent.Infrastructure.Persistence.Conventions;

// PostgreSQL rejects U+0000 in text and the \u0000 escape in jsonb, and extracted PDF text occasionally contains it.
internal static partial class StoreText
{
    public static string RemoveNul(string value) =>
        value.Contains('\0') ? value.Replace("\0", string.Empty, StringComparison.Ordinal) : value;

    public static string RemoveNulFromJson(string json)
    {
        var withoutRaw = RemoveNul(json);
        return withoutRaw.Contains(@"\u0000", StringComparison.Ordinal)
            ? NulEscape().Replace(withoutRaw, string.Empty)
            : withoutRaw;
    }

    // Matches \u0000 only when its backslash is not itself escaped.
    [GeneratedRegex(@"(?<=(?:^|[^\\])(?:\\\\)*)\\u0000", RegexOptions.CultureInvariant)]
    private static partial Regex NulEscape();
}
