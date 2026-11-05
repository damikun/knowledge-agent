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
    // Inngest stores queued and delayed jobs; the in-process runner loses them when the process stops.
    public bool KeepsJobsAcrossRestarts => Name == "inngest";

    // The dev server and a self-hosted server open a run at /run; Inngest Cloud also needs the environment in the path.
    public string? RunUrl(string? runId) =>
        runId is { Length: > 0 } && DashboardUrl is { Length: > 0 } dashboard && dashboard != InngestJobEngine.CloudDashboardUrl
            ? $"{dashboard.TrimEnd('/')}/run?runID={Uri.EscapeDataString(runId)}"
            : null;
}
