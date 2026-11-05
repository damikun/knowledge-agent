using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Feedback;

public static class FeedbackModule
{
    public static IHostApplicationBuilder AddFeedback(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IFeedbackRepository, FeedbackRepository>();
        services.TryAddScoped<IFeedbackService, FeedbackService>();
        services.TryAddScoped<IFeedbackRecorder>(provider => provider.GetRequiredService<IFeedbackService>());
        services.AddJob<FeedbackScoreJob>(FeedbackScoreJob.Kind, new JobPolicy { MaxConcurrency = 2, MaxAttempts = 5 });
        return builder;
    }
}
