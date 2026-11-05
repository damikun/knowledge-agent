using System.Collections.Frozen;
using System.Globalization;

namespace KnowledgeAgent.Features.Prompts.Rendering;

// Deliberately tolerant: malformed input never throws, scanning simply stops.
internal static class TemplateSyntax
{
    public static readonly FrozenSet<string> LiteralKeywords = FrozenSet.ToFrozenSet(["true", "false", "null", "undefined"], StringComparer.Ordinal);

    private static readonly FrozenSet<string> BlocksRequiringArgument = FrozenSet.ToFrozenSet(["if", "unless", "each", "with"], StringComparer.Ordinal);

    // Finds mistakes that Handlebars.Net silently renders as nothing, such as {{#if}} without a condition.
    public static string? FindProblem(string template)
    {
        foreach (var rawTag in ReadTags(template))
        {
            var tag = TrimWhitespaceControl(rawTag);
            string? helper = null;
            List<Token> tokens;
            if (tag.StartsWith('#'))
            {
                tokens = Tokenize(tag[1..]);
                helper = tokens.Count > 0 && tokens[0] is PathToken head ? head.Text : null;
            }
            else
            {
                tokens = Tokenize(tag);
                if (tokens is [PathToken { Text: "else" }, PathToken chained, ..])
                {
                    tokens = tokens[1..];
                    helper = chained.Text;
                }
            }

            if (helper is not null && BlocksRequiringArgument.Contains(helper) && !tokens.Skip(1).Any(t => t is not HashToken))
            {
                return $"'{{{{#{helper}}}}}' needs an argument, for example {{{{#{helper} value}}}}";
            }
        }

        return null;
    }

    public static string TrimWhitespaceControl(string rawTag)
    {
        var tag = rawTag.Trim();
        if (tag.StartsWith('~'))
        {
            tag = tag[1..].TrimStart();
        }

        if (tag.EndsWith('~'))
        {
            tag = tag[..^1].TrimEnd();
        }

        return tag;
    }

    public static IEnumerable<string> ReadTags(string template)
    {
        var index = 0;
        while (index < template.Length)
        {
            var open = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (open < 0)
            {
                yield break;
            }

            // \{{literal}} is printed as is, while \\{{x}} is an escaped backslash followed by a real tag.
            if (open > 0 && template[open - 1] == '\\' && !(open > 1 && template[open - 2] == '\\'))
            {
                index = SkipPast(template, open + 2, "}}");
                continue;
            }

            // Raw blocks {{{{raw}}}} ... {{{{/raw}}}} are not parsed.
            if (At(template, open, "{{{{"))
            {
                var rawClose = template.IndexOf("{{{{/", open + 4, StringComparison.Ordinal);
                index = rawClose < 0 ? template.Length : SkipPast(template, rawClose, "}}}}");
                continue;
            }

            var triple = At(template, open, "{{{");
            var start = open + (triple ? 3 : 2);
            var probe = start < template.Length && template[start] == '~' ? start + 1 : start;
            if (At(template, probe, "!--"))
            {
                index = SkipLongComment(template, probe + 3);
                continue;
            }

            if (At(template, probe, "!"))
            {
                index = SkipPast(template, probe + 1, "}}");
                continue;
            }

            var closing = triple ? "}}}" : "}}";
            var close = FindClosing(template, start, closing);
            if (close < 0)
            {
                yield break;
            }

            yield return template[start..close];
            index = close + closing.Length;
        }
    }

    public static List<Token> Tokenize(string text)
    {
        var position = 0;
        return ReadTokens(text, ref position, nested: false);
    }

    public static bool IsNumber(string word) =>
        word.Length > 0
        && (char.IsAsciiDigit(word[0]) || (word[0] == '-' && word.Length > 1 && char.IsAsciiDigit(word[1])))
        && double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool At(string text, int index, string value) =>
        index >= 0 && index + value.Length <= text.Length && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static int SkipPast(string text, int from, string terminator)
    {
        var end = text.IndexOf(terminator, from, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + terminator.Length;
    }

    private static int SkipLongComment(string text, int from)
    {
        var position = from;
        while (true)
        {
            var end = text.IndexOf("--", position, StringComparison.Ordinal);
            if (end < 0)
            {
                return text.Length;
            }

            var after = end + 2;
            if (At(text, after, "}}"))
            {
                return after + 2;
            }

            if (At(text, after, "~}}"))
            {
                return after + 3;
            }

            position = end + 1;
        }
    }

    private static int FindClosing(string text, int start, string closing)
    {
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (At(text, i, closing))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<Token> ReadTokens(string text, ref int position, bool nested)
    {
        var tokens = new List<Token>();
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (c == ')')
            {
                position++;
                if (nested)
                {
                    return tokens;
                }

                continue;
            }

            if (c == '(')
            {
                position++;
                tokens.Add(new SubExpressionToken(ReadTokens(text, ref position, nested: true)));
                continue;
            }

            if (c == '|')
            {
                // Block parameters: {{#each items as |item index|}}.
                var end = text.IndexOf('|', position + 1);
                position = end < 0 ? text.Length : end + 1;
                if (tokens.Count > 0 && tokens[^1] is PathToken { Text: "as" })
                {
                    tokens.RemoveAt(tokens.Count - 1);
                }

                continue;
            }

            var token = ReadSimpleToken(text, ref position);
            var lookahead = position;
            while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
            {
                lookahead++;
            }

            if (token is PathToken key && lookahead < text.Length && text[lookahead] == '=')
            {
                position = lookahead + 1;
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                {
                    position++;
                }

                Token? value = null;
                if (position < text.Length)
                {
                    if (text[position] == '(')
                    {
                        position++;
                        value = new SubExpressionToken(ReadTokens(text, ref position, nested: true));
                    }
                    else
                    {
                        value = ReadSimpleToken(text, ref position);
                    }
                }

                tokens.Add(new HashToken(key.Text, value));
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static Token ReadSimpleToken(string text, ref int position)
    {
        var c = text[position];
        if (c is '"' or '\'')
        {
            var start = ++position;
            while (position < text.Length && text[position] != c)
            {
                position += text[position] == '\\' ? 2 : 1;
            }

            var literal = text[start..Math.Min(position, text.Length)];
            position = Math.Min(position + 1, text.Length);
            return new LiteralToken(literal);
        }

        var begin = position;
        while (position < text.Length)
        {
            var ch = text[position];
            if (ch == '[')
            {
                var end = text.IndexOf(']', position + 1);
                position = end < 0 ? text.Length : end + 1;
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '(' or ')' or '=' or '|')
            {
                break;
            }

            position++;
        }

        if (position == begin)
        {
            // Consume a stray character such as '=' so that scanning always advances.
            position++;
            return new LiteralToken(text[begin..position]);
        }

        var word = text[begin..position];
        return IsNumber(word) || LiteralKeywords.Contains(word) ? new LiteralToken(word) : new PathToken(word);
    }

    internal abstract record Token;

    internal sealed record PathToken(string Text) : Token;

    internal sealed record LiteralToken(string Text) : Token;

    internal sealed record SubExpressionToken(IReadOnlyList<Token> Tokens) : Token;

    internal sealed record HashToken(string Key, Token? Value) : Token;
}
