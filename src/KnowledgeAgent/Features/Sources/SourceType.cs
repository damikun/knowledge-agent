namespace KnowledgeAgent.Features.Sources;

public enum SourceType
{
    Url,
    Pdf,
    Text,
    Markdown,

    // Crawls a site; each page it finds becomes a Url source with this one as its parent.
    Website,
}
