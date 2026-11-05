using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Overview;

public static class OverviewModule
{
    public static IHostApplicationBuilder AddOverview(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<IOverviewRepository, OverviewRepository>();
        return builder;
    }
}
