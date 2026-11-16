using System.Globalization;
using System.Text;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal static class TextNormalizer
{
    public const char PageSeparator = '\f';

    public static string Normalize(string? text, bool keepPageSeparators = false, bool preserveIndentation = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (keepPageSeparators && text.Contains(PageSeparator))
        {
            var pages = text.Split(PageSeparator);
            for (var i = 0; i < pages.Length; i++)
            {
                pages[i] = NormalizeBlock(pages[i], preserveIndentation);
            }

            return string.Join(PageSeparator, pages);
        }

        return NormalizeBlock(text, preserveIndentation);
    }

    private static string NormalizeBlock(string text, bool preserveIndentation)
    {
        var cleaned = Clean(text);
        var output = new StringBuilder(cleaned.Length);
        var blankLinePending = false;
        var line = new StringBuilder();

        foreach (var rawLine in cleaned.Split('\n'))
        {
            CollapseLine(rawLine, preserveIndentation, line);
            if (line.Length == 0)
            {
                blankLinePending = output.Length > 0;
                continue;
            }

            if (output.Length > 0)
            {
                output.Append(blankLinePending ? "\n\n" : "\n");
            }

            blankLinePending = false;
            output.Append(line);
        }

        return output.ToString();
    }

    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                    builder.Append('\n');
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n':
                case '\u0085':
                case '\u2028':
                    builder.Append('\n');
                    break;
                case PageSeparator:
                case '\v':
                case '\u2029':
                    builder.Append("\n\n");
                    break;
                case '\t':
                    builder.Append('\t');
                    break;
                case '\u00AD':
                case '\u200B':
                case '\u2060':
                case '\uFEFF':
                    break;
                case '\uDB40' when i + 1 < text.Length && text[i + 1] is >= '\uDC00' and <= '\uDC7F':
                    i++;
                    break;
                case var hidden when IsHidden(hidden):
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append(' ');
                    }
                    else if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator)
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsHidden(char c) => c is '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u180E'
        or (>= '\u2061' and <= '\u2064') or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    private static void CollapseLine(string line, bool preserveIndentation, StringBuilder output)
    {
        output.Clear();
        var start = 0;
        if (preserveIndentation)
        {
            while (start < line.Length && line[start] is ' ' or '\t')
            {
                start++;
            }

            if (start == line.Length)
            {
                return;
            }

            output.Append(line, 0, start);
        }

        var contentStart = output.Length;
        var pendingSpace = false;
        for (var i = start; i < line.Length; i++)
        {
            var c = line[i];
            if (c is ' ' or '\t')
            {
                pendingSpace = output.Length > contentStart;
                continue;
            }

            if (pendingSpace)
            {
                output.Append(' ');
                pendingSpace = false;
            }

            output.Append(c);
        }

        if (output.Length == contentStart)
        {
            output.Clear();
        }
    }
}
