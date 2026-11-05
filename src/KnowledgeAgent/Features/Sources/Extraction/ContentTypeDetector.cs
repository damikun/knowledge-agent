using System.Net.Http.Headers;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal static class ContentTypeDetector
{
    private const int SniffLength = 2048;

    // PDF readers accept junk before the %PDF- header within the first 1024 bytes.
    public static bool LooksLikePdf(ReadOnlySpan<byte> content) =>
        content[..Math.Min(content.Length, 1024)].IndexOf("%PDF-"u8) >= 0;

    public static bool LooksLikeText(ReadOnlySpan<byte> content)
    {
        if (TextDecoding.HasUnicodeBom(content))
        {
            return true;
        }

        return !LooksLikePdf(content) && content[..Math.Min(content.Length, 8192)].IndexOf((byte)0) < 0;
    }

    public static ContentKind Detect(ReadOnlySpan<byte> content, string? mediaType, string? fileName)
    {
        if (LooksLikePdf(content))
        {
            return ContentKind.Pdf;
        }

        var fromExtension = FromExtension(fileName);
        switch (FromMediaType(mediaType))
        {
            case ContentKind.Pdf:
                // Trusted even without a PDF header so that the PDF reader reports a clear "invalid PDF" error.
                return ContentKind.Pdf;
            case ContentKind.Html:
                return ContentKind.Html;
            case ContentKind.Markdown:
                return ContentKind.Markdown;
            case ContentKind.PlainText:
                // Raw file hosts serve markdown and HTML files as text/plain.
                return fromExtension is ContentKind.Markdown or ContentKind.Html ? fromExtension : ContentKind.PlainText;
        }

        if (IsBinaryMediaType(mediaType))
        {
            return ContentKind.Unknown;
        }

        return fromExtension != ContentKind.Unknown ? fromExtension : Sniff(content);
    }

    public static ContentKind FromMediaType(string? mediaType)
    {
        var normalized = NormalizeMediaType(mediaType);
        return normalized switch
        {
            null => ContentKind.Unknown,
            "application/pdf" or "application/x-pdf" => ContentKind.Pdf,
            "text/html" or "application/xhtml+xml" => ContentKind.Html,
            "text/markdown" or "text/x-markdown" => ContentKind.Markdown,
            _ when normalized.StartsWith("text/", StringComparison.Ordinal) => ContentKind.PlainText,
            _ => ContentKind.Unknown,
        };
    }

    public static ContentKind FromExtension(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return ContentKind.Unknown;
        }

        var extension = Path.GetExtension(fileNameOrPath.Trim()).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => ContentKind.Pdf,
            ".html" or ".htm" or ".xhtml" => ContentKind.Html,
            ".md" or ".markdown" => ContentKind.Markdown,
            ".txt" or ".text" => ContentKind.PlainText,
            _ => ContentKind.Unknown,
        };
    }

    public static string? NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return MediaTypeHeaderValue.TryParse(contentType, out var parsed) && !string.IsNullOrWhiteSpace(parsed.MediaType)
            ? parsed.MediaType.ToLowerInvariant()
            : null;
    }

    private static bool IsBinaryMediaType(string? mediaType)
    {
        var normalized = NormalizeMediaType(mediaType);
        if (normalized is null)
        {
            return false;
        }

        return normalized.StartsWith("image/", StringComparison.Ordinal)
            || normalized.StartsWith("audio/", StringComparison.Ordinal)
            || normalized.StartsWith("video/", StringComparison.Ordinal)
            || normalized.StartsWith("font/", StringComparison.Ordinal)
            || normalized.StartsWith("application/vnd.", StringComparison.Ordinal)
            || normalized is "application/zip" or "application/x-zip-compressed" or "application/gzip" or "application/x-gzip"
                or "application/x-tar" or "application/x-7z-compressed" or "application/x-rar-compressed" or "application/msword"
                or "application/wasm" or "application/x-msdownload";
    }

    private static ContentKind Sniff(ReadOnlySpan<byte> content)
    {
        if (!LooksLikeText(content))
        {
            return ContentKind.Unknown;
        }

        var head = TextDecoding.Decode(content[..Math.Min(content.Length, SniffLength)], charSet: null)
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<head", StringComparison.OrdinalIgnoreCase))
        {
            return ContentKind.Html;
        }

        return ContentKind.PlainText;
    }
}
