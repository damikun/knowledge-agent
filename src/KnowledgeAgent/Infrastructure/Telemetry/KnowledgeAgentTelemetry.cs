namespace KnowledgeAgent.Infrastructure.Telemetry;

// Names must start with "KnowledgeAgent." to match the wildcard subscription in ServiceDefaults.
public static class KnowledgeAgentTelemetry
{
    public const string Prefix = "KnowledgeAgent";
    public const string Agents = "KnowledgeAgent.Agents";
    public const string AI = "KnowledgeAgent.AI";
    public const string Ingestion = "KnowledgeAgent.Ingestion";
    public const string Search = "KnowledgeAgent.Search";
    public const string Prompts = "KnowledgeAgent.Prompts";
    public const string VectorStore = "KnowledgeAgent.VectorStore";
    public const string Evaluation = "KnowledgeAgent.Evaluation";
    public const string Jobs = "KnowledgeAgent.Jobs";

    public const string ChatTurnSpanName = "chat.turn";

    public const string LangfusePromptNameKey = "langfuse.prompt.name";
    public const string LangfusePromptVersionKey = "langfuse.prompt.version";
}
