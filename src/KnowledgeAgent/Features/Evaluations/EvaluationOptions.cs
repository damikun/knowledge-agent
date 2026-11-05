namespace KnowledgeAgent.Features.Evaluations;

public sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    public string? DatasetsPath { get; set; }
    public int Parallelism { get; set; } = 3;
    public int MaxItemsPerRun { get; set; } = 500;
    public bool RecordInLangfuse { get; set; } = true;
    public string LangfuseEnvironment { get; set; } = Running.EvalRunner.DefaultEnvironment;
    public bool KeepLiveConversations { get; set; }
}
