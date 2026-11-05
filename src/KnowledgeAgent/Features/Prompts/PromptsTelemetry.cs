using System.Diagnostics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Prompts;

internal static class PromptsTelemetry
{
    public const string FetchActivityName = "prompt.fetch";
    public const string RenderActivityName = "prompt.render";

    public const string PromptNameTag = "prompt.name";
    public const string PromptVersionTag = "prompt.version";
    public const string PromptOriginTag = "prompt.origin";
    public const string PromptSelectorTag = "prompt.selector";
    public const string PromptTypeTag = "prompt.type";
    public const string PromptFoundTag = "prompt.found";

    public static readonly ActivitySource ActivitySource = new(KnowledgeAgentTelemetry.Prompts);
}
