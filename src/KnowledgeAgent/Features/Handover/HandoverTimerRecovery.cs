using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Handover;

// The in-process runner loses delayed jobs on restart, so pending timers are requeued at startup (Inngest keeps its own).
// The jobs end handovers with a conditional update, so a timer that did survive cannot apply twice.
internal sealed partial class HandoverTimerRecovery(
    IServiceScopeFactory scopeFactory,
    IJobQueue jobs,
    JobEngineInfo engine,
    TimeProvider timeProvider,
    ILogger<HandoverTimerRecovery> logger) : IStartupInitializer
{
    public int Order => StartupOrder.Ingestion + 5;

    public string Name => "Handover timer recovery";

    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (engine.KeepsJobsAcrossRestarts)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var pending = await ListPendingAsync(scope.ServiceProvider, cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var widgets = scope.ServiceProvider.GetRequiredService<IWidgetRepository>();
        var email = await scope.ServiceProvider.GetRequiredService<IIntegrationSettings>().GetEmailAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var overdue = 0;
        foreach (var handover in pending)
        {
            var widget = handover.WidgetId is { } widgetId ? await widgets.GetAsync(widgetId, cancellationToken) : null;
            var policy = widget?.Settings.Handover ?? new WidgetHandover();
            var expireAt = handover.RequestedAt.AddMinutes(policy.WaitMinutes);
            var key = handover.Id.ToString();

            // Once it expires, the expiry emails the transcript itself, so an escalation would only race it.
            if (expireAt > now && handover is { Channel: WidgetChoices.HandoverSlack, EscalatedAt: null } && email.CanSend)
            {
                await jobs.EnqueueAsync(HandoverJobs.Escalate, key, handover.RequestedAt.AddMinutes(policy.EscalateAfterMinutes), cancellationToken);
            }

            await jobs.EnqueueAsync(HandoverJobs.Expire, key, expireAt, cancellationToken);
            overdue += expireAt <= now ? 1 : 0;
        }

        LogRecovered(logger, pending.Count, overdue);
    }

    private static async Task<IReadOnlyList<Handover>> ListPendingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<KnowledgeDbContext>();
        using var _ = await services.GetRequiredService<DbContextGate>().EnterAsync(cancellationToken);
        return await db.Handovers.AsNoTracking()
            .Where(handover => handover.Status == HandoverStatus.Pending)
            .OrderBy(handover => handover.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued the timers of {Count} pending handovers again; {Overdue} were overdue and expire now")]
    private static partial void LogRecovered(ILogger logger, int count, int overdue);
}
