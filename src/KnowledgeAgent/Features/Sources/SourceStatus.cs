namespace KnowledgeAgent.Features.Sources;

public enum SourceStatus
{
    Pending,
    Processing,
    Ready,
    Failed,

    Waiting,

    NeedsBrowser,
}
