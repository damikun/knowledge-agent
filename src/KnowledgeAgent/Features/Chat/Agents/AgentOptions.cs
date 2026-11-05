namespace KnowledgeAgent.Features.Chat.Agents;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public string AppName { get; set; } = "Knowledge Agent";
    public string OrchestratorProfile { get; set; } = "orchestrator";
    public string ResearcherProfile { get; set; } = "researcher";
    public string UtilityProfile { get; set; } = "utility";
    public int MaxIterations { get; set; } = 8;
    public int ResearcherMaxIterations { get; set; } = 6;
    public bool ResearcherEnabled { get; set; } = true;
    public bool AllowGeneralKnowledge { get; set; } = true;
    public bool GenerateTitles { get; set; } = true;
    public int DefaultTopK { get; set; } = 6;
    public int MaxTopK { get; set; } = 20;
    public int MaxToolResultChars { get; set; } = 12000;
    public int ToolTimeoutSeconds { get; set; } = 120;
}
