namespace KnowledgeAgent.Features.Prompts.Seeding;

internal sealed class PromptSeedingSettings
{
    public TimeSpan InitialAttemptTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(10);
}
