using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Widgets;

public static class WidgetsModule
{
    public static IHostApplicationBuilder AddWidgets(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<WidgetOptions>().Bind(builder.Configuration.GetSection(WidgetOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IWidgetRepository, WidgetRepository>();
        services.TryAddScoped<IWidgetService, WidgetService>();
        services.TryAddScoped<VisitorService>();
        services.TryAddSingleton<VisitorTokens>();
        services.TryAddSingleton<WidgetIdentity>();
        services.TryAddSingleton<WidgetRateLimiter>();
        services.TryAddSingleton<WidgetAssetFiles>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentUsage, WidgetAgentUsage>());

        services.AddJob<WidgetRetentionJob>(WidgetRetentionJob.Kind, JobPolicy.Default with { Overlap = JobOverlap.Skip });
        services.AddJobSchedule(new JobScheduleRegistration("widget-retention", WidgetRetentionJob.Kind, "all", "20 3 * * *", Enabled: true));
        services.AddJob<CloseIdleConversationsJob>(CloseIdleConversationsJob.Kind, JobPolicy.Default with { Overlap = JobOverlap.Skip });
        services.AddJobSchedule(new JobScheduleRegistration("close-idle-conversations", CloseIdleConversationsJob.Kind, "all", "*/5 * * * *", Enabled: true));
        return builder;
    }
}

internal sealed class WidgetAgentUsage(IWidgetRepository widgets) : IAgentUsage
{
    public async Task<IReadOnlyList<string>> ListUsesAsync(Guid agentId, CancellationToken cancellationToken = default) =>
        (await widgets.ListByAgentAsync(agentId, cancellationToken)).Select(widget => $"the widget '{widget.Name}'").ToList();
}

internal sealed partial class WidgetRetentionJob(
    IWidgetRepository widgets,
    IConversationRepository conversations,
    IPeopleRepository people,
    TimeProvider timeProvider,
    ILogger<WidgetRetentionJob> logger) : IJobHandler
{
    public const string Kind = "widgets.retention";

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var widget in await widgets.ListAsync(cancellationToken))
        {
            var cutoff = now.AddDays(-widget.Settings.Security.RetentionDays);
            var deleted = await conversations.DeleteWidgetConversationsAsync(widget.Id, cutoff, cancellationToken);
            var visitors = await widgets.DeleteVisitorsSeenBeforeAsync(widget.Id, cutoff, cancellationToken);
            if (deleted > 0 || visitors > 0)
            {
                LogDeleted(logger, deleted, visitors, widget.Id, widget.Settings.Security.RetentionDays);
            }
        }

        var contacts = await people.TidyAsync(cancellationToken);
        if (contacts > 0)
        {
            LogContactsDeleted(logger, contacts);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} anonymous people without conversations")]
    private static partial void LogContactsDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Conversations} conversations and {Visitors} visitors of widget {WidgetId} older than {Days} days")]
    private static partial void LogDeleted(ILogger logger, int conversations, int visitors, Guid widgetId, int days);
}

internal sealed partial class CloseIdleConversationsJob(
    IWidgetRepository widgets,
    IConversationRepository conversations,
    IHandoverService handover,
    ChatTurnHost turns,
    TimeProvider timeProvider,
    ILogger<CloseIdleConversationsJob> logger) : IJobHandler
{
    public const string Kind = "conversations.close-idle";
    public const int BatchSize = 100;

    // A visitor who wrote last is waiting for the team, so only staff replies and notices let it close.
    private static readonly MessageAuthor[] VisitorNotWaiting = [MessageAuthor.Staff, MessageAuthor.System];

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var widget in await widgets.ListAsync(cancellationToken))
        {
            var settings = widget.Settings.Conversations;
            if (!settings.CloseIdle)
            {
                continue;
            }

            var closed = await CloseAsync(widget.Id, ConversationStatus.Ai, now.AddMinutes(-settings.AiIdleMinutes), lastAuthors: null, cancellationToken)
                + await CloseAsync(widget.Id, ConversationStatus.Human, now.AddHours(-settings.HumanIdleHours), VisitorNotWaiting, cancellationToken);
            if (closed > 0)
            {
                LogClosed(logger, closed, widget.Id);
            }
        }
    }

    private async Task<int> CloseAsync(
        Guid widgetId,
        ConversationStatus status,
        DateTimeOffset idleSince,
        IReadOnlyCollection<MessageAuthor>? lastAuthors,
        CancellationToken cancellationToken)
    {
        var closed = 0;
        while (true)
        {
            var batch = await conversations.ListIdleAsync(widgetId, status, idleSince, lastAuthors, BatchSize, cancellationToken);
            var closedInBatch = 0;
            foreach (var conversationId in batch)
            {
                // An answer being written on this instance is activity that is not stored yet.
                if (!turns.IsActive(conversationId) && await handover.CloseIdleAsync(conversationId, status, idleSince, cancellationToken))
                {
                    closedInBatch++;
                }
            }

            closed += closedInBatch;

            // A batch that closed nothing would come back the same.
            if (batch.Count < BatchSize || closedInBatch == 0)
            {
                return closed;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Closed {Count} quiet conversations of widget {WidgetId}")]
    private static partial void LogClosed(ILogger logger, int count, Guid widgetId);
}
