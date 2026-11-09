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

    // Whether the answer says the knowledge base does not answer the question, in the usual ways models phrase it.
    public static bool Declines(string? answer) =>
        !string.IsNullOrWhiteSpace(answer) && DeclinePatterns().Any(pattern => pattern.IsMatch(Normalize(answer)));

    private static IEnumerable<Regex> DeclinePatterns()
    {
        yield return NotFound();
        yield return DoesNotSay();
        yield return HasNoInformation();
        yield return NoInformationAbout();
        yield return NotInTheSources();
        yield return NoSource();
        yield return DoNotKnow();
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
            // Markdown emphasis, so "**27** days" reads as "27 days".
            if (character == '*')
            {
                continue;
            }

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

    [GeneratedRegex(@"\b(?:could ?n't|could not|can't|cannot|can not|did ?n't|did not|was ?n't able to|was not able to|were not able to|am not able to|(?:was |am |were )?unable to)\s+(?:find|locate|identify|see|confirm)\b")]
    private static partial Regex NotFound();

    [GeneratedRegex(@"\b(?:does ?n't|does not|do ?n't|do not|did ?n't|did not)\s+(?:give|say|mention|contain|cover|include|list|state|specify|provide|document|describe|name|show|have)\b")]
    private static partial Regex DoesNotSay();

    [GeneratedRegex(@"\b(?:has|have|had|contains?|there is|there's|there are|i found|found)\s+no\s+(?:\w+\s+)?(?:information|info|details?|mention|records?|data|documentation|sources?|passages?|answer)\b")]
    private static partial Regex HasNoInformation();

    [GeneratedRegex(@"\bno\s+(?:\w+\s+)?(?:information|info|details|mention|records?|data|documentation)\s+(?:on|about|of|regarding|for|in)\b")]
    private static partial Regex NoInformationAbout();

    [GeneratedRegex(@"(?:\bnot|n't)\s+(?:\w+\s+)?(?:in|covered|mentioned|documented|listed|included|available|found|specified|stated|described)\s+(?:in|by|anywhere in)?\s*(?:the|your|our|any of the|any)?\s*(?:knowledge base|knowledge|documents?|docs|documentation|sources?|corpus|passages?|materials?)\b")]
    private static partial Regex NotInTheSources();

    [GeneratedRegex(@"\bno\s+(?:\w+\s+)?sources?\b")]
    private static partial Regex NoSource();

    [GeneratedRegex(@"\bi\s+(?:do ?n't|do not)\s+know\b")]
    private static partial Regex DoNotKnow();

    [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSeparator();

    [GeneratedRegex(@"([€$£¥])\s+(?=\d)")]
    private static partial Regex CurrencySpace();
}
