using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Labels;

public static class LabelsModule
{
    public static IHostApplicationBuilder AddLabels(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ILabelRepository, LabelRepository>();
        services.TryAddScoped<ILabelService, LabelService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFirstAnswerHook, LabelFirstAnswerHook>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, LabelSeedInitializer>());
        services.AddJob<AutoLabelJob>(AutoLabelJob.Kind, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 3 });
        return builder;
    }
}

internal sealed class LabelFirstAnswerHook(IJobQueue jobs) : IFirstAnswerHook
{
    public Task OnFirstAnswerAsync(Conversation conversation, CancellationToken cancellationToken) =>
        conversation.Channel == ConversationChannel.Portal
            ? jobs.EnqueueAsync(AutoLabelJob.Kind, conversation.Id.ToString(), cancellationToken: cancellationToken)
            : Task.CompletedTask;
}

// The in-memory store starts empty, so seed it here; PostgreSQL got the defaults once from the migration.
internal sealed partial class LabelSeedInitializer(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LabelSeedInitializer> logger) : IStartupInitializer
{
    public int Order => StartupOrder.Labels;

    public string Name => "Default labels";

    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().Database.IsRelational())
        {
            return;
        }

        var labels = scope.ServiceProvider.GetRequiredService<ILabelRepository>();
        if (await labels.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (id, name, color, description) in LabelDefaults.All)
        {
            await labels.AddAsync(
                new Label { Id = id, Name = name, Color = color, Description = description, AutoApply = true, CreatedAt = now, UpdatedAt = now },
                cancellationToken);
        }

        LogSeeded(logger, LabelDefaults.All.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Added {Count} default labels")]
    private static partial void LogSeeded(ILogger logger, int count);
}
