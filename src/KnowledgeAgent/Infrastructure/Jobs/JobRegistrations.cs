using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgeAgent.Infrastructure.Jobs;

public sealed record JobRegistration(string Kind, Type HandlerType, JobPolicy Policy)
{
    public bool HandlesFailure => typeof(IJobFailureHandler).IsAssignableFrom(HandlerType);
}

public sealed record JobScheduleRegistration(string Name, string Kind, string Key, string Cron, bool Enabled);

public static class JobServiceCollectionExtensions
{
    public static IServiceCollection AddJob<THandler>(
        this IServiceCollection services,
        string kind,
        JobPolicy? policy = null)
        where THandler : class, IJobHandler
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (services.Any(descriptor => descriptor.ImplementationInstance is JobRegistration existing && existing.Kind == kind))
        {
            return services;
        }

        var registration = new JobRegistration(kind, typeof(THandler), policy ?? JobPolicy.Default);
        services.TryAddScoped<THandler>();
        services.AddSingleton(registration);
        InngestJobEngine.Find(services)?.AddFunction(registration);
        return services;
    }

    public static IServiceCollection AddJobSchedule(this IServiceCollection services, JobScheduleRegistration schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!services.Any(descriptor => descriptor.ImplementationInstance is JobScheduleRegistration existing && existing.Name == schedule.Name))
        {
            services.AddSingleton(schedule);
            InngestJobEngine.Find(services)?.AddSchedule(schedule);
        }

        return services;
    }
}
