using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Handover;

public static class HandoverModule
{
    // Depends on Integrations (email, Slack), so add it after them.
    public static IHostApplicationBuilder AddHandover(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<HandoverOptions>().Bind(builder.Configuration.GetSection(HandoverOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<IHandoverRepository, HandoverRepository>();
        services.TryAddScoped<IHandoverService, HandoverService>();
        services.TryAddScoped<IHandoverRequester>(provider => provider.GetRequiredService<IHandoverService>());
        services.TryAddScoped<HandoverContextLoader>();
        services.TryAddScoped<SlackInbound>();
        services.TryAddSingleton<VisitorPresence>();

        services.TryAddSingleton<SlackSocketModeService>();
        services.TryAddSingleton<ISlackConnectionState>(provider => provider.GetRequiredService<SlackSocketModeService>());
        services.AddHostedService(provider => provider.GetRequiredService<SlackSocketModeService>());

        // Send jobs are idempotent per key, so a key queued twice never sends twice.
        // The Slack update just re-renders the current state, so a repeat is harmless.
        services.AddJob<HandoverDispatchJob>(HandoverJobs.Dispatch, new JobPolicy { MaxConcurrency = 4, MaxAttempts = 6, Idempotent = true });
        services.AddJob<HandoverEscalateJob>(HandoverJobs.Escalate, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 3, Idempotent = true });
        services.AddJob<HandoverExpireJob>(HandoverJobs.Expire, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 3, Idempotent = true });
        services.AddJob<HandoverRelayJob>(HandoverJobs.Relay, new JobPolicy { MaxConcurrency = 4, MaxAttempts = 5, Idempotent = true });
        services.AddJob<VisitorNotifyJob>(HandoverJobs.NotifyVisitor, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 3, Idempotent = true });
        services.AddJob<SlackUpdateJob>(HandoverJobs.UpdateSlack, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 3 });
        // A slow poll skips the next tick instead of queueing it.
        services.AddJob<MailPollJob>(HandoverJobs.PollMail, new JobPolicy { MaxConcurrency = 1, MaxAttempts = 1, Overlap = JobOverlap.Skip });
        services.AddJobSchedule(new JobScheduleRegistration("handover-mail-poll", HandoverJobs.PollMail, "inbox", "* * * * *", Enabled: true));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, HandoverTimerRecovery>());
        return builder;
    }
}
