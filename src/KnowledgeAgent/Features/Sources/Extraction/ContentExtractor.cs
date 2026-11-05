using KnowledgeAgent.Features.Sources.Ingestion;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal sealed class ContentExtractor(HtmlTextExtractor htmlExtractor, PdfTextExtractor pdfExtractor)
{
    public ExtractedDocument Extract(byte[] content, ContentDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        var kind = descriptor.KindHint != ContentKind.Unknown
            ? descriptor.KindHint
            : ContentTypeDetector.Detect(content, descriptor.MediaType, descriptor.FileName);

        return kind switch
        {
            ContentKind.Pdf => pdfExtractor.Extract(content, cancellationToken),
            ContentKind.Html => htmlExtractor.Extract(TextDecoding.DecodeHtml(content, descriptor.CharSet), descriptor.Uri),
            ContentKind.PlainText or ContentKind.Markdown => ExtractText(content, descriptor.CharSet, kind),
            _ => throw new IngestionException(
                $"Unsupported content type '{ContentTypeDetector.NormalizeMediaType(descriptor.MediaType) ?? "unknown"}'. Supported: HTML, PDF, plain text and markdown.",
                isPermanent: true),
        };
    }

    public static ExtractedDocument FromText(string text, ContentKind kind)
    {
        if (kind == ContentKind.Pdf)
        {
            var normalized = TextNormalizer.Normalize(text, keepPageSeparators: true);
            return new ExtractedDocument(normalized, Title: null, normalized.Count(c => c == TextNormalizer.PageSeparator) + 1, kind);
        }

        return new ExtractedDocument(TextNormalizer.Normalize(text, preserveIndentation: true), Title: null, PageCount: null, kind);
    }

    private static ExtractedDocument ExtractText(byte[] content, string? charSet, ContentKind kind)
    {
        if (!ContentTypeDetector.LooksLikeText(content))
        {
            throw new IngestionException("The content is not readable text.", isPermanent: true);
        }

        return FromText(TextDecoding.Decode(content, charSet), kind);
    }
}
