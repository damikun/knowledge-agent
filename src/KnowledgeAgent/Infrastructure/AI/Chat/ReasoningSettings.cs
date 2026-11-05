using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Chat;

internal static class ReasoningSettings
{
    public const string EffortNone = "none";
    public const string EffortLow = "low";
    public const string EffortMedium = "medium";
    public const string EffortHigh = "high";
    public const string EffortExtraHigh = "extra_high";

    public const string OutputNone = "none";
    public const string OutputSummary = "summary";
    public const string OutputFull = "full";

    public static string? NormalizeEffort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "none" or "off" or "disabled" => EffortNone,
            "minimal" or "low" => EffortLow,
            "medium" => EffortMedium,
            "high" => EffortHigh,
            "extra_high" or "extrahigh" or "xhigh" or "very_high" or "max" => EffortExtraHigh,
            _ => normalized,
        };
    }

    public static string? NormalizeOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "none" or "omitted" or "hidden" or "off" => OutputNone,
            "summary" or "summarized" or "summaries" => OutputSummary,
            "full" or "all" => OutputFull,
            _ => normalized,
        };
    }

    public static ReasoningEffort? ToEffort(string? value) => NormalizeEffort(value) switch
    {
        EffortNone => ReasoningEffort.None,
        EffortLow => ReasoningEffort.Low,
        EffortMedium => ReasoningEffort.Medium,
        EffortHigh => ReasoningEffort.High,
        EffortExtraHigh => ReasoningEffort.ExtraHigh,
        _ => null,
    };

    public static ReasoningOutput? ToOutput(string? value) => NormalizeOutput(value) switch
    {
        OutputNone => ReasoningOutput.None,
        OutputSummary => ReasoningOutput.Summary,
        OutputFull => ReasoningOutput.Full,
        _ => null,
    };

    public static ReasoningOptions? ToReasoningOptions(string? effort, string? output)
    {
        var mappedEffort = ToEffort(effort);
        var mappedOutput = ToOutput(output);
        return mappedEffort is null && mappedOutput is null
            ? null
            : new ReasoningOptions { Effort = mappedEffort, Output = mappedOutput };
    }
}
