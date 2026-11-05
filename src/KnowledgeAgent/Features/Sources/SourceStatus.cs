namespace KnowledgeAgent.Features.Sources;

public enum SourceStatus
{
    Pending,
    Processing,
    Ready,
    Failed,

    // No embedding model is configured. The source is queued again once one is.
    Waiting,

    // A crawled page with almost no text without JavaScript, such as the shell of a single-page app.
    NeedsBrowser,
}
