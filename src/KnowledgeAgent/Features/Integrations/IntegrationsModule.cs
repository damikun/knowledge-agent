using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgeAgent.Features.Integrations;

public static class IntegrationsModule
{
    // Handover and Alerts send through these, so add it first.
    public static IHostApplicationBuilder AddIntegrations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<IntegrationOptions>().Bind(builder.Configuration.GetSection(IntegrationOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIntegrationSettings, IntegrationSettingsStore>();
        services.TryAddScoped<IPublicAddress, PublicAddressStore>();
        services.TryAddSingleton<IEmailTransport, MailKitEmailTransport>();
        services.AddHttpClient<ISlackGateway, SlackWebApiGateway>(client =>
        {
            client.BaseAddress = SlackWebApiGateway.BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.TryAddScoped<IntegrationService>();
        return builder;
    }
}
