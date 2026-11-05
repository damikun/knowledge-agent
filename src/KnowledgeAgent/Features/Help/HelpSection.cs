namespace KnowledgeAgent.Features.Help;

public enum HelpOrigin
{
    Guide,
    Docs,
}

public sealed record HelpSection(
    string Id,
    string Document,
    string DocumentTitle,
    IReadOnlyList<string> Headings,
    string Text,
    string Summary,
    IReadOnlyList<string> Links,
    string? Link,
    HelpOrigin Origin)
{
    public string Title => Headings.Count > 0 ? Headings[^1] : DocumentTitle;

    public string Path => string.Join(" > ", new[] { DocumentTitle }.Concat(Headings));

    public Guid Key { get; } = HelpIds.For(Id);
}
