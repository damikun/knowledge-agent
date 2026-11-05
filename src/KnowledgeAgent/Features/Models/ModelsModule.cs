using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Models;

public static class ModelsModule
{
    public static IHostApplicationBuilder AddModels(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IProviderSettingsService, ProviderSettingsService>();
        return builder;
    }
}
