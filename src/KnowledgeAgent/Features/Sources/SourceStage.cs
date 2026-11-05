namespace KnowledgeAgent.Features.Sources;

// The step an ingestion attempt is on. Set only while the source is Processing.
public enum SourceStage
{
    Fetching,
    Extracting,
    Chunking,
    Contextualizing,
    Embedding,
    Storing,
}
