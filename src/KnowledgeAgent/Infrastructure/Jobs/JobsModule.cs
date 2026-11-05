using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.Jobs;

public static class JobsModule
{
    public const string InngestHealthCheckName = "inngest";

    public static IHostApplicationBuilder AddJobs(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobEngineInfo)))
        {
            return builder;
        }

        var section = builder.Configuration.GetSection(JobsOptions.SectionName);
        var settings = section.Get<JobsOptions>() ?? new JobsOptions();
        services.AddOptions<JobsOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        if (JobEngines.IsInngest(settings.Provider))
        {
            InngestJobEngine.Add(builder, settings);
            services.AddSingleton(new JobEngineInfo("inngest", InngestJobEngine.ResolveDashboardUrl(settings.Inngest, builder.Configuration)));
            // Degraded rather than Unhealthy: chat keeps working while jobs wait for Inngest.
            services.AddHealthChecks().AddInngest(name: InngestHealthCheckName, failureStatus: HealthStatus.Degraded);
        }
        else
        {
            services.TryAddSingleton<InProcessJobRunner>();
            services.TryAddSingleton<IJobQueue>(serviceProvider => serviceProvider.GetRequiredService<InProcessJobRunner>());
            services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<InProcessJobRunner>());
            services.AddHostedService<InProcessScheduler>();
            services.AddSingleton(new JobEngineInfo("inprocess", DashboardUrl: null));
        }

        return builder;
    }
}
