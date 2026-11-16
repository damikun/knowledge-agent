namespace KnowledgeAgent.Infrastructure.Jobs;

public static class JobEngines
{
    public const string Inngest = "Inngest";
    public const string InProcess = "InProcess";

    public static bool IsInngest(string? provider) => string.Equals(provider, Inngest, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? provider) =>
        IsInngest(provider) || string.Equals(provider, InProcess, StringComparison.OrdinalIgnoreCase);
}

public sealed record JobEngineInfo(string Name, string? DashboardUrl)
{
    public bool KeepsJobsAcrossRestarts => Name == "inngest";

    public string? RunUrl(string? runId) =>
        runId is { Length: > 0 } && DashboardUrl is { Length: > 0 } dashboard && dashboard != InngestJobEngine.CloudDashboardUrl
            ? $"{dashboard.TrimEnd('/')}/run?runID={Uri.EscapeDataString(runId)}"
            : null;
}
