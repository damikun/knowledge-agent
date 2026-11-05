using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Analytics;

public static class AnalyticsModule
{
    public static IHostApplicationBuilder AddAnalytics(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<IAnalyticsRepository, AnalyticsRepository>();
        return builder;
    }
}
