using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public string Provider { get; set; } = JobEngines.InProcess;
    public InngestJobsOptions Inngest { get; set; } = new();
    public Dictionary<string, JobScheduleOptions> Schedules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class InngestJobsOptions
{
    public string AppId { get; set; } = "knowledge-agent";

    // Where the portal links to the Inngest dashboard; derived from INNGEST_DEV or INNGEST_BASE_URL when empty.
    public string? DashboardUrl { get; set; }
}

public sealed class JobScheduleOptions
{
    public string? Cron { get; set; }
    public bool? Enabled { get; set; }
}

internal sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        var errors = new List<string>();
        if (!JobEngines.IsKnown(options.Provider))
        {
            errors.Add($"Jobs:Provider '{options.Provider}' is not supported. Use {JobEngines.Inngest} or {JobEngines.InProcess}.");
        }

        if (JobEngines.IsInngest(options.Provider) && string.IsNullOrWhiteSpace(options.Inngest.AppId))
        {
            errors.Add("Jobs:Inngest:AppId is required when Jobs:Provider is Inngest.");
        }

        if (options.Inngest.DashboardUrl is { Length: > 0 } dashboard
            && !(Uri.TryCreate(dashboard, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            errors.Add("Jobs:Inngest:DashboardUrl must be an absolute http or https URL.");
        }

        foreach (var (schedule, settings) in options.Schedules)
        {
            if (settings.Cron is { } cron && !JobSchedules.IsValidCron(cron))
            {
                errors.Add($"Jobs:Schedules:{schedule}:Cron '{cron}' is not a valid cron expression (minute hour day month weekday).");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
