using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.Jobs;

// Used when Inngest does not run the schedules. Occurrences missed while the process was down are not made up.
internal sealed partial class InProcessScheduler(
    IEnumerable<JobScheduleRegistration> schedules,
    IJobQueue queue,
    IOptionsMonitor<JobsOptions> options,
    TimeProvider timeProvider,
    ILogger<InProcessScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (string Cron, DateTimeOffset Due)> _next = new(StringComparer.Ordinal);

    internal async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var queued = 0;
        foreach (var schedule in schedules.Select(registration => JobSchedules.Resolve(registration, options.CurrentValue)))
        {
            if (!schedule.Enabled || JobSchedules.NextOccurrence(schedule.Cron, now) is not { } upcoming)
            {
                _next.Remove(schedule.Name);
                continue;
            }

            if (!_next.TryGetValue(schedule.Name, out var next) || next.Cron != schedule.Cron)
            {
                _next[schedule.Name] = (schedule.Cron, upcoming);
                continue;
            }

            if (next.Due > now)
            {
                continue;
            }

            await queue.EnqueueAsync(schedule.Kind, schedule.Key, cancellationToken: cancellationToken);
            _next[schedule.Name] = (schedule.Cron, upcoming);
            queued++;
            LogQueued(logger, schedule.Name, schedule.Kind, upcoming);
        }

        return queued;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogTickFailed(logger, ex);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {Schedule} queued a {Kind} job; next run at {NextRunAt}")]
    private static partial void LogQueued(ILogger logger, string schedule, string kind, DateTimeOffset nextRunAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking job schedules failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
