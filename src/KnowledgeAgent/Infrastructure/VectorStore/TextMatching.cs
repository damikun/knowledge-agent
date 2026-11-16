using System.Buffers;
using System.Globalization;
using System.Text;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed record NormalizedTextSearch(
    string Pattern,
    string Needle,
    bool MatchCase,
    Guid[]? SourceIds,
    int Skip,
    int Take)
{
    public bool MatchesNothing => Pattern.Length == 0;
}

internal static class TextMatching
{
    public const int MaxTextLength = 200;

    private const string WordStart = @"(?:\A|[^\p{L}\p{N}])";
    private const string WordEnd = @"(?:\z|[^\p{L}\p{N}])";
    private const string AnyWhitespace = @"[\s\p{Z}]+";

    public static string Clean(string? text) =>
        string.Concat((text ?? string.Empty).Where(character => !char.IsControl(character) || char.IsWhiteSpace(character))).Trim();

    public static string Pattern(string text, bool matchCase, bool wholeWords)
    {
        var words = Words(text);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(matchCase ? string.Empty : "(?i)");
        if (wholeWords && Rune.DecodeFromUtf16(words[0], out var first, out _) == OperationStatus.Done && IsWordCharacter(first))
        {
            builder.Append(WordStart);
        }

        builder.AppendJoin(AnyWhitespace, words.Select(Escape));
        if (wholeWords && Rune.DecodeLastFromUtf16(words[^1], out var last, out _) == OperationStatus.Done && IsWordCharacter(last))
        {
            builder.Append(WordEnd);
        }

        return builder.ToString();
    }

    public static string Needle(string text) => Words(text).MaxBy(word => word.Length) ?? string.Empty;

    internal static string Escape(string word)
    {
        var builder = new StringBuilder(word.Length * 2);
        foreach (var character in word)
        {
            if (char.IsAscii(character) && !char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsWordCharacter(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
        or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
}
