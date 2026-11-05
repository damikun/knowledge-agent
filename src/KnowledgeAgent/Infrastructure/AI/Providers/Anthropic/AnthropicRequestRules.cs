using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;

// Current Claude models reject these settings with HTTP 400, so they are corrected with a one-time warning instead.
internal static class AnthropicRequestRules
{
    public static void Apply(ChatOptions options, AnthropicThinkingMode thinkingMode, bool defaultEffort, OneTimeWarnings warnings)
    {
        RemoveSamplingParameters(options, warnings);
        NormalizeReasoning(options, thinkingMode, defaultEffort, warnings);
        RemoveForcedToolChoice(options, warnings);
    }

    public static void RemoveSamplingParameters(ChatOptions options, OneTimeWarnings warnings)
    {
        if (options.Temperature is not null)
        {
            warnings.Warn("anthropic.temperature", "Claude models reject sampling parameters; the configured temperature is not sent to Anthropic.");
            options.Temperature = null;
        }

        if (options.TopP is not null)
        {
            warnings.Warn("anthropic.top_p", "Claude models reject sampling parameters; top_p is not sent to Anthropic.");
            options.TopP = null;
        }

        if (options.TopK is not null)
        {
            warnings.Warn("anthropic.top_k", "Claude models reject sampling parameters; top_k is not sent to Anthropic.");
            options.TopK = null;
        }
    }

    public static void NormalizeReasoning(ChatOptions options, AnthropicThinkingMode thinkingMode, bool defaultEffort, OneTimeWarnings warnings)
    {
        var effort = options.Reasoning?.Effort;
        var output = options.Reasoning?.Output;

        if (thinkingMode == AnthropicThinkingMode.Adaptive && effort == ReasoningEffort.None)
        {
            warnings.Warn("anthropic.effort_none", "Claude models always think and reject disabled thinking; reasoning effort 'none' is sent as 'low'.");
            effort = ReasoningEffort.Low;
        }

        if (defaultEffort && effort is null)
        {
            effort = ReasoningEffort.Medium;
        }

        // New instance because the caller's ReasoningOptions may be shared with other ChatOptions clones.
        options.Reasoning = effort is null && output is null ? null : new ReasoningOptions { Effort = effort, Output = output };
    }

    public static void RemoveForcedToolChoice(ChatOptions options, OneTimeWarnings warnings)
    {
        if (options.ToolMode is RequiredChatToolMode)
        {
            warnings.Warn("anthropic.tool_choice", "Claude models reject forced tool choice; tool mode 'required' is sent as 'auto'.");
            options.ToolMode = ChatToolMode.Auto;
        }
    }
}
