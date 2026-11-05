using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Search;

internal static partial class QueryTermAnalyzer
{
    public const int MaxTerms = 16;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "about", "above", "after", "again", "against", "all", "also", "am", "an", "and", "any", "are", "as", "at",
        "be", "been", "before", "being", "below", "between", "both", "but", "by", "can", "could", "did", "do", "does",
        "doing", "down", "during", "each", "else", "etc", "few", "for", "from", "further", "get", "gets", "got", "had",
        "has", "have", "having", "he", "her", "here", "hers", "him", "his", "how", "i", "if", "in", "into", "is", "it",
        "its", "itself", "just", "may", "me", "might", "more", "most", "much", "many", "must", "my", "myself", "no",
        "nor", "not", "now", "of", "off", "on", "once", "only", "or", "other", "our", "ours", "out", "over", "own",
        "please", "same", "shall", "she", "should", "so", "some", "such", "than", "that", "the", "their", "theirs",
        "them", "then", "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up",
        "us", "very", "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom", "whose", "why",
        "will", "with", "would", "you", "your", "yours", "yourself", "i'm", "dont",
    };

    public static IReadOnlyList<string> Terms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var text = query.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string term)
        {
            if (terms.Count < MaxTerms && seen.Add(term))
            {
                terms.Add(term);
            }
        }

        foreach (Match match in Token().Matches(text))
        {
            var token = match.Value;
            if (IsCompound(token))
            {
                Add(token);
                foreach (var part in token.Split(['-', '.', '_', '/'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length >= 3 && IsLetters(part) && !Stopwords.Contains(part))
                    {
                        Add(Stem(part));
                    }
                }

                continue;
            }

            if (token.Length < 2 || Stopwords.Contains(token))
            {
                continue;
            }

            Add(IsLetters(token) ? Stem(token) : token);
        }

        return terms;
    }

    // The stores match a term at the start of a word, so a stem only has to be a prefix of its inflections.
    internal static string Stem(string word)
    {
        static bool Keeps(string word, int cut, int minimum) => word.Length - cut >= minimum;

        if (word.EndsWith("ies", StringComparison.Ordinal) && Keeps(word, 3, 4))
        {
            return word[..^3];
        }

        if (word.EndsWith("es", StringComparison.Ordinal) && Keeps(word, 2, 3)
            && (word.EndsWith("ses", StringComparison.Ordinal) || word.EndsWith("xes", StringComparison.Ordinal)
                || word.EndsWith("zes", StringComparison.Ordinal) || word.EndsWith("ches", StringComparison.Ordinal)
                || word.EndsWith("shes", StringComparison.Ordinal)))
        {
            return word[..^2];
        }

        if (word.EndsWith("ing", StringComparison.Ordinal) && Keeps(word, 3, 4))
        {
            return word[..^3];
        }

        if (word.EndsWith("ed", StringComparison.Ordinal) && Keeps(word, 2, 4))
        {
            return word[..^2];
        }

        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal)
            && !word.EndsWith("is", StringComparison.Ordinal) && Keeps(word, 1, 3))
        {
            return Stem(word[..^1]) is var singular && singular.Length >= 3 ? singular : word[..^1];
        }

        if ((word.EndsWith('y') || word.EndsWith('e')) && Keeps(word, 1, 4))
        {
            return word[..^1];
        }

        return word;
    }

    private static bool IsCompound(string token) => token.IndexOfAny(['-', '.', '_', '/']) >= 0;

    private static bool IsLetters(string token) => token.All(char.IsLetter);

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[-._/][\p{L}\p{N}]+)*")]
    private static partial Regex Token();
}
