namespace KnowledgeAgent.Features.Prompts;

public sealed class PromptOptions
{
    public const string SectionName = "Prompts";

    public string Store { get; set; } = "Langfuse";
    public string Label { get; set; } = "production";
    public int CacheTtlSeconds { get; set; } = 60;
    public bool SeedOnStartup { get; set; } = true;
    public bool FallbackToLocal { get; set; } = true;
}
