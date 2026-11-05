using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Alerts;

public static class AlertsModule
{
    // Depends on Integrations, Handover, Analytics and the AI providers, so add it after them.
    public static IHostApplicationBuilder AddAlerts(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SlackOutageTracker>();
        services.TryAddSingleton<AlertQualityCache>();
        services.TryAddScoped<IAlertRepository, AlertRepository>();
        services.TryAddScoped<IAlertSettingsStore, AlertSettingsStore>();
        services.TryAddScoped<AlertEvaluator>();
        services.TryAddScoped<AlertService>();

        services.AddJob<AlertNotifyJob>(AlertNotifyJob.Kind, new JobPolicy { MaxConcurrency = 1, MaxAttempts = 1, Overlap = JobOverlap.Skip });
        services.AddJobSchedule(new JobScheduleRegistration(AlertNotifyJob.ScheduleName, AlertNotifyJob.Kind, "all", "*/5 * * * *", Enabled: true));
        return builder;
    }
}
