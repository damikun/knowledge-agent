using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Help;

internal static class HelpIds
{
    // A name-based id (version 8 layout), so the same section always gets the same id.
    public static Guid For(string name)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);
        var bytes = hash[..16].ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

internal static partial class HelpMarkdown
{
    public const int MaxSectionChars = 2_400;
    public const int MaxSummaryChars = 160;

    public static IReadOnlyList<HelpSection> Parse(string document, string markdown, HelpOrigin origin)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var title = Title(markdown) ?? document;
        var sections = new List<(List<string> Headings, StringBuilder Text)>();
        var current = (Headings: new List<string>(), Text: new StringBuilder());
        string? h2 = null;
        var inFence = false;
        var seenTitle = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && Heading().Match(line) is { Success: true } heading)
            {
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                if (level == 1 && !seenTitle)
                {
                    seenTitle = true;
                    continue;
                }

                if (level is 2 or 3)
                {
                    sections.Add(current);
                    if (level == 2)
                    {
                        h2 = text;
                        current = ([text], new StringBuilder());
                    }
                    else
                    {
                        current = (h2 is null ? [text] : [h2, text], new StringBuilder());
                    }

                    continue;
                }
            }

            current.Text.Append(line).Append('\n');
        }

        sections.Add(current);

        var documentLink = FirstLink(markdown);
        var result = new List<HelpSection>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (headings, text) in sections)
        {
            var body = text.ToString().Trim();
            if (body.Length == 0 || IsOnlyComments(body))
            {
                continue;
            }

            var parts = Split(body);
            for (var i = 0; i < parts.Count; i++)
            {
                var baseId = $"{origin.ToString().ToLowerInvariant()}/{document}" + (headings.Count > 0 ? "#" + Slug(headings[^1]) : string.Empty);
                var id = i == 0 ? baseId : $"{baseId}-{i + 1}";
                for (var n = 2; !usedIds.Add(id); n++)
                {
                    id = $"{baseId}-{n}";
                }

                var links = PortalLinks(parts[i]).Distinct(StringComparer.Ordinal).ToList();
                var link = links.FirstOrDefault() ?? FirstLink(body) ?? (origin == HelpOrigin.Guide ? documentLink : null);
                result.Add(new HelpSection(id, document, title, headings, parts[i], Summary(parts[i]), links, link, origin));
            }
        }

        return result;
    }

    public static string? Title(string markdown)
    {
        var inFence = false;
        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence && Heading().Match(line) is { Success: true } heading && heading.Groups[1].Value.Length == 1)
            {
                return heading.Groups[2].Value.Trim();
            }
        }

        return null;
    }

    public static IEnumerable<string> PortalLinks(string markdown) =>
        PortalLink().Matches(markdown).Select(match => match.Groups[1].Value);

    public static string? FirstLink(string markdown) => PortalLinks(markdown).FirstOrDefault();

    public static string Plain(string markdown)
    {
        var text = Comment().Replace(markdown, " ");
        text = LinkSyntax().Replace(text, "$1");
        text = text.Replace("**", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
        text = ListMarker().Replace(text, string.Empty);
        return Whitespace().Replace(text, " ").Trim();
    }

    public static string Summary(string markdown)
    {
        var text = Fence().Replace(markdown, " ");
        text = TableRule().Replace(text, " ").Replace('|', ' ');
        var plain = Plain(text);
        var end = SentenceEnd().Match(plain);
        var sentence = end.Success ? plain[..(end.Index + 1)] : plain;
        if (sentence.Length <= MaxSummaryChars)
        {
            return sentence;
        }

        var cut = sentence.LastIndexOf(' ', MaxSummaryChars - 1);
        return sentence[..(cut < MaxSummaryChars / 2 ? MaxSummaryChars - 1 : cut)].TrimEnd(',', '.', ';', ':') + "…";
    }

    public static string Slug(string heading)
    {
        var builder = new StringBuilder(heading.Length);
        foreach (var character in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool IsOnlyComments(string body) => Comment().Replace(body, string.Empty).Trim().Length == 0;

    private static List<string> Split(string body)
    {
        if (body.Length <= MaxSectionChars)
        {
            return [body];
        }

        var parts = new List<string>();
        var builder = new StringBuilder();
        foreach (var paragraph in body.Split("\n\n"))
        {
            if (builder.Length > 0 && builder.Length + paragraph.Length > MaxSectionChars)
            {
                parts.Add(builder.ToString().Trim());
                builder.Clear();
            }

            builder.Append(paragraph).Append("\n\n");
        }

        if (builder.Length > 0)
        {
            parts.Add(builder.ToString().Trim());
        }

        return parts;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\]\((/[^)\s]*)\)")]
    private static partial Regex PortalLink();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkSyntax();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(?m)^\s*(?:[-*]|\d+\.)\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?ms)^\s*(```|~~~).*?^\s*\1[^\n]*$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"(?m)^\s*\|?[\s:|-]*-{3,}[\s:|-]*$")]
    private static partial Regex TableRule();

    // The end of a sentence: a full stop, question or exclamation mark before a space, unless it ends "e.g." and the like.
    [GeneratedRegex(@"(?<!\b(?:e\.g|i\.e|etc|vs))[.!?](?=\s|$)")]
    private static partial Regex SentenceEnd();
}
