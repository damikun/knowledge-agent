using System.Text.RegularExpressions;

namespace KnowledgeAgent.Infrastructure.Web;

// Error text shown in the portal: one line, with credentials and URL query strings masked.
internal static partial class ErrorMessages
{
    public static string Redact(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        // Only the first line is kept because providers sometimes append response bodies or diagnostics.
        var text = message.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        text = BearerToken().Replace(text, "Bearer ***");
        text = SecretAssignment().Replace(text, "$1=***");
        text = KeyLikeToken().Replace(text, "***");
        text = UrlQuery().Replace(text, "$1");
        return WhiteSpace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(api[_-]?key|apikey|access[_-]?token|refresh[_-]?token|token|secret|client[_-]?secret|password|passwd|pwd|authorization|signature|sig)\s*[=:]\s*[""']?[^\s""'&,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_-]{12,}|\bAKIA[0-9A-Z]{16}\b|\bgh[pousr]_[A-Za-z0-9]{20,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex KeyLikeToken();

    [GeneratedRegex(@"(https?://[^\s?#""'<>]+)\?[^\s""'<>]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlQuery();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
