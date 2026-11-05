using System.Globalization;
using System.Text;

namespace KnowledgeAgent.Features.Chat.Agents;

internal static class TextUtil
{
    public const char Ellipsis = '…';

    private const int TruncationMarkerReserve = 64;

    public static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    public static string Shorten(string text, int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        var keep = SafeLength(text, maxChars - 1);
        return text[..keep].TrimEnd() + Ellipsis;
    }

    public static string TruncateToolOutput(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var keep = SafeLength(text, Math.Max(0, maxChars - TruncationMarkerReserve));
        return text[..keep] + string.Create(CultureInfo.InvariantCulture, $"\n[… output truncated: {text.Length - keep} more characters]");
    }

    public static string Clip(string? text, int maxChars) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= maxChars ? text : text[..SafeLength(text, maxChars)] + Ellipsis;

    private static int SafeLength(string text, int length)
    {
        length = Math.Clamp(length, 0, text.Length);
        return length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
    }
}
