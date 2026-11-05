using System.Text.Json.Nodes;
using KnowledgeAgent.Infrastructure.AI.Profiles;

namespace KnowledgeAgent.Features.Prompts;

public static class PromptNames
{
    public const string OrchestratorSystem = "orchestrator-system";
    public const string ResearcherSystem = "researcher-system";
    public const string ReplySuggestion = "reply-suggestion";
    public const string ConversationTitle = "conversation-title";
    public const string ConversationLabels = "conversation-labels";
    public const string EvalJudge = "eval-judge";
    public const string Rerank = "rerank";

    public static readonly IReadOnlyList<string> All = [OrchestratorSystem, ResearcherSystem, ReplySuggestion, ConversationTitle, ConversationLabels, EvalJudge, Rerank];
}

public enum PromptType
{
    Text,
    Chat,
}

public enum PromptOrigin
{
    Langfuse,
    Local,
}

public sealed record PromptMessage(string Role, string Content);

public sealed record PromptTemplate
{
    public required string Name { get; init; }
    public int Version { get; init; } = 1;
    public PromptType Type { get; init; } = PromptType.Text;
    public string? Text { get; init; }
    public IReadOnlyList<PromptMessage> Messages { get; init; } = [];
    public JsonObject? Config { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public PromptOrigin Origin { get; init; }

    // The message the version was saved with in Langfuse; the seeder recognizes its own versions by it.
    public string? CommitMessage { get; init; }
}

public sealed record RenderedPrompt
{
    public required string Name { get; init; }
    public int Version { get; init; }
    public PromptOrigin Origin { get; init; }
    public PromptType Type { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<PromptMessage> Messages { get; init; } = [];
    public JsonObject? Config { get; init; }
    public ModelProfilePatch? ModelPatch { get; init; }
    public string? Profile { get; init; }
}

public sealed record PromptSelector(string? Label = null, int? Version = null);

public sealed record PromptSummary(
    string Name,
    PromptOrigin Origin,
    int Version,
    PromptType Type,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Variables,
    string? ExternalUrl);
