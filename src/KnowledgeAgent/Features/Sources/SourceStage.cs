namespace KnowledgeAgent.Features.Sources;

public enum SourceStage
{
    Fetching,
    Extracting,
    Chunking,
    Contextualizing,
    Embedding,
    Storing,
}
