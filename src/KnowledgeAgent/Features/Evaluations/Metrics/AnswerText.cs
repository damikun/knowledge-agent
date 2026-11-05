using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Evaluations.Metrics;

public static partial class AnswerText
{
    internal const int MinSubstantiveWords = 6;

    public static IReadOnlyList<string> Citations(string? answer)
    {
        if (string.IsNullOrEmpty(answer))
        {
            return [];
        }

        var references = new List<string>();
        foreach (Match match in CitationGroup().Matches(answer))
        {
            foreach (Match reference in CitationReference().Matches(match.Groups["refs"].Value))
            {
                var normalized = "S" + int.Parse(reference.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
                if (!references.Contains(normalized, StringComparer.Ordinal))
                {
                    references.Add(normalized);
                }
            }
        }

        return references;
    }

    public static bool ContainsFact(string? answer, string fact)
    {
        if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(fact))
        {
            return false;
        }

        var text = Normalize(answer);
        return fact.Split('|')
            .Select(alternative => Normalize(alternative))
            .Where(alternative => alternative.Length > 0)
            .Any(alternative => ContainsOnBoundaries(text, alternative));
    }

    private static bool ContainsOnBoundaries(string text, string value)
    {
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            var startsCleanly = index == 0 || !char.IsLetterOrDigit(value[0]) || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + value.Length;
            var endsCleanly = end == text.Length || !char.IsLetterOrDigit(value[^1]) || !char.IsLetterOrDigit(text[end]);
            if (startsCleanly && endsCleanly)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> SubstantiveSegments(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return [];
        }

        var segments = new List<string>();
        var paragraph = new StringBuilder();
        var inCode = false;

        void Flush()
        {
            if (paragraph.Length > 0)
            {
                segments.Add(paragraph.ToString().Trim());
                paragraph.Clear();
            }
        }

        foreach (var rawLine in answer.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#') || TableSeparator().IsMatch(line) || IsEmphasizedNotice(line))
            {
                Flush();
                continue;
            }

            if (ListItem().IsMatch(line) || line.StartsWith('|'))
            {
                Flush();
                segments.Add(line);
                continue;
            }

            paragraph.Append(' ').Append(line);
        }

        Flush();
        return segments.Where(segment => WordCount(segment) >= MinSubstantiveWords).ToList();
    }

    public static double? CitationCoverage(string? answer)
    {
        var segments = SubstantiveSegments(answer);
        return segments.Count == 0 ? null : segments.Count(segment => Citations(segment).Count > 0) / (double)segments.Count;
    }

    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            builder.Append(character switch
            {
                '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-',
                '‘' or '’' or '‚' or '′' => '\'',
                '“' or '”' or '„' or '″' => '"',
                _ when char.IsWhiteSpace(character) => ' ',
                _ => char.ToLowerInvariant(character),
            });
        }

        var text = Whitespace().Replace(builder.ToString(), " ").Trim();
        text = ThousandsSeparator().Replace(text, string.Empty);
        return CurrencySpace().Replace(text, "$1");
    }

    private static int WordCount(string segment)
    {
        var withoutCitations = CitationGroup().Replace(segment, " ");
        return Word().Matches(withoutCitations).Count;
    }

    private static bool IsEmphasizedNotice(string line) =>
        line.Length > 2
        && ((line.StartsWith('_') && line.EndsWith('_')) || (line.StartsWith('*') && line.EndsWith('*') && !line.StartsWith("* ", StringComparison.Ordinal)));

    [GeneratedRegex(@"\[(?<refs>\s*S\d+\s*(?:[,;]\s*S\d+\s*)*)\]", RegexOptions.IgnoreCase)]
    private static partial Regex CitationGroup();

    [GeneratedRegex(@"S(?<number>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CitationReference();

    [GeneratedRegex(@"^([-*+]|\d+[.)])\s+")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\|?\s*:?-{3,}")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’.,-]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSeparator();

    [GeneratedRegex(@"([€$£¥])\s+(?=\d)")]
    private static partial Regex CurrencySpace();
}
