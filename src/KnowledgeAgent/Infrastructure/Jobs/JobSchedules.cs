using Cronos;

namespace KnowledgeAgent.Infrastructure.Jobs;

public sealed record EffectiveSchedule(string Name, string Kind, string Key, string Cron, bool Enabled);

public static class JobSchedules
{
    // The code default, changed by Jobs:Schedules:<name> in configuration.
    public static EffectiveSchedule Resolve(JobScheduleRegistration registration, JobsOptions options)
    {
        var configured = options.Schedules.GetValueOrDefault(registration.Name);
        return new EffectiveSchedule(
            registration.Name,
            registration.Kind,
            registration.Key,
            configured?.Cron ?? registration.Cron,
            configured?.Enabled ?? registration.Enabled);
    }

    public static bool IsValidCron(string cron) => CronExpression.TryParse(cron, CronFormat.Standard, out _);

    public static DateTimeOffset? NextOccurrence(string cron, DateTimeOffset after) =>
        CronExpression.Parse(cron, CronFormat.Standard).GetNextOccurrence(after, TimeZoneInfo.Utc);
}
