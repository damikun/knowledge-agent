using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal static partial class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool HasUnicodeBom(ReadOnlySpan<byte> content) => TryGetBomEncoding(content, out _, out _);

    public static string Decode(ReadOnlySpan<byte> content, string? charSet)
    {
        if (TryGetBomEncoding(content, out var bomEncoding, out var bomLength))
        {
            return bomEncoding.GetString(content[bomLength..]);
        }

        if (GetEncoding(charSet) is { } declared)
        {
            return declared.GetString(content);
        }

        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return (GetEncoding("windows-1252") ?? Encoding.Latin1).GetString(content);
        }
    }

    public static string DecodeHtml(ReadOnlySpan<byte> content, string? charSet)
    {
        if (TryGetBomEncoding(content, out var bomEncoding, out var bomLength))
        {
            return bomEncoding.GetString(content[bomLength..]);
        }

        var encoding = GetEncoding(charSet) ?? GetEncoding(SniffMetaCharset(content));
        return encoding is not null ? encoding.GetString(content) : Decode(content, charSet: null);
    }

    public static Encoding? GetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim().Trim('"', '\'');
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(trimmed) ?? Encoding.GetEncoding(trimmed);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? SniffMetaCharset(ReadOnlySpan<byte> content)
    {
        var head = Encoding.Latin1.GetString(content[..Math.Min(content.Length, 4096)]);
        var match = MetaCharset().Match(head);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static bool TryGetBomEncoding(ReadOnlySpan<byte> content, out Encoding encoding, out int bomLength)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            (encoding, bomLength) = (new UTF8Encoding(false), 3);
            return true;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            (encoding, bomLength) = (new UTF32Encoding(bigEndian: false, byteOrderMark: false), 4);
            return true;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            (encoding, bomLength) = (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4);
            return true;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            (encoding, bomLength) = (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2);
            return true;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            (encoding, bomLength) = (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2);
            return true;
        }

        (encoding, bomLength) = (Encoding.UTF8, 0);
        return false;
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([A-Za-z0-9_\-:.]+)|<\?xml[^>]+encoding\s*=\s*["']([A-Za-z0-9_\-:.]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaCharset();
}
