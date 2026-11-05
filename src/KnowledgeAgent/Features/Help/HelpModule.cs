using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Help;

public static class HelpModule
{
    // Reads model profiles, sources, agents and widgets, so add it after them.
    public static IHostApplicationBuilder AddHelp(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<HelpOptions>().BindConfiguration(HelpOptions.SectionName);
        services.TryAddSingleton<IHelpSource, BundledHelpSource>();
        services.TryAddSingleton<HelpLibrary>();
        services.TryAddSingleton<HelpPrompt>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, HelpIndexInitializer>());
        services.TryAddScoped<IHelpSettingsStore, HelpSettingsStore>();
        services.TryAddScoped<HelpSetupReader>();
        services.TryAddScoped<HelpChatService>();
        return builder;
    }
}
