namespace KnowledgeAgent.Features.Sources.Extraction;

internal enum ContentKind
{
    Unknown,
    Html,
    Pdf,
    PlainText,
    Markdown,
}

internal sealed record ContentDescriptor(
    string? MediaType = null,
    string? CharSet = null,
    string? FileName = null,
    Uri? Uri = null,
    ContentKind KindHint = ContentKind.Unknown);

internal sealed record ExtractedDocument(
    string Text,
    string? Title,
    int? PageCount,
    ContentKind Kind,
    string? SiteName = null)
{
    public bool HasPages => Kind == ContentKind.Pdf;
}
