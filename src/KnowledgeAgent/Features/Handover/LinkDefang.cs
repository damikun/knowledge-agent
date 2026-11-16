using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Handover;

internal static partial class LinkDefang
{
    public static string ForSlack(string text) =>
        Address().Replace(text, match => "`" + match.Value.Replace("`", "'", StringComparison.Ordinal) + "`");

    public static string ForEmail(string text) =>
        Address().Replace(text, match =>
        {
            var value = SchemeHttp().Replace(match.Value, "hxxp");
            return value.Replace(".", "[.]", StringComparison.Ordinal);
        });

    [GeneratedRegex(
        @"(?:\b(?:https?|ftp)://|\bwww\.)[^\s<>`""]+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}\b(?:/[^\s<>`""]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Address();

    [GeneratedRegex(@"^http", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeHttp();
}
