using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Handover;

// Text the model wrote reaches the team in Slack and email, where addresses turn into links by themselves.
// These keep them readable but not clickable, so a planted passage cannot send the team a phishing link.
internal static partial class LinkDefang
{
    // Slack does not link text inside inline code. Expects text already escaped for Slack.
    public static string ForSlack(string text) =>
        Address().Replace(text, match => "`" + match.Value.Replace("`", "'", StringComparison.Ordinal) + "`");

    // Mail clients link "https://" and "www." and bare domains; hxxp and [.] stop all of them.
    public static string ForEmail(string text) =>
        Address().Replace(text, match =>
        {
            var value = SchemeHttp().Replace(match.Value, "hxxp");
            return value.Replace(".", "[.]", StringComparison.Ordinal);
        });

    // A scheme or www. address, or a bare host name with a top-level domain, up to the next space.
    [GeneratedRegex(
        @"(?:\b(?:https?|ftp)://|\bwww\.)[^\s<>`""]+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}\b(?:/[^\s<>`""]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Address();

    [GeneratedRegex(@"^http", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeHttp();
}
