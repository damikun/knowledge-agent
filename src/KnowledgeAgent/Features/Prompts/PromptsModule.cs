using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Prompts.Rendering;
using KnowledgeAgent.Features.Prompts.Seeding;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Telemetry;

namespace KnowledgeAgent.Features.Prompts;

public static class PromptsModule
{
    public static IHostApplicationBuilder AddPrompts(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PromptsModuleMarker)))
        {
            return builder;
        }

        services.AddSingleton<PromptsModuleMarker>();

        services.AddOptions<PromptOptions>()
            .BindConfiguration(PromptOptions.SectionName)
            .Validate(
                options => PromptStoreMode.IsLocal(options)
                    || string.Equals(options.Store?.Trim(), PromptStoreMode.Langfuse, StringComparison.OrdinalIgnoreCase),
                "Prompts:Store must be 'Langfuse' or 'Local'.")
            .Validate(options => options.CacheTtlSeconds >= 0, "Prompts:CacheTtlSeconds must not be negative.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Label), "Prompts:Label must not be empty.")
            .ValidateOnStart();
        services.AddOptions<LangfuseOptions>().BindConfiguration(LangfuseOptions.SectionName);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PromptSeedingSettings>();
        services.TryAddSingleton<LocalPromptCatalog>();
        services.TryAddSingleton<IPromptTemplateRenderer, HandlebarsPromptRenderer>();

        services.AddHttpClient<LangfuseClient>((serviceProvider, client) =>
                LangfuseClient.ConfigureHttpClient(client, serviceProvider.GetRequiredService<IOptions<LangfuseOptions>>().Value))
            // The typed client is held by singletons, so DNS changes are picked up by recycling pooled
            // connections rather than handlers.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(ConfigureResilience);

        // Langfuse is often unreachable while it boots and the fallback already warns;
        // without this Polly logs every failed attempt with a stack trace.
        services.ConfigureAll<TelemetryOptions>(options =>
        {
            var inner = options.SeverityProvider;
            options.SeverityProvider = args =>
                args.Source.PipelineName?.StartsWith(nameof(LangfuseClient), StringComparison.Ordinal) == true
                    ? ResilienceEventSeverity.Debug
                    : inner?.Invoke(args) ?? args.Event.Severity;
        });

        services.TryAddSingleton<LangfuseLinkBuilder>();
        services.TryAddSingleton<PromptService>();
        services.TryAddSingleton<IPromptService>(serviceProvider => serviceProvider.GetRequiredService<PromptService>());
        services.TryAddSingleton<PromptEditor>();

        services.TryAddSingleton<PromptSeeder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, PromptSeedingInitializer>());
        services.AddHostedService<PromptSeedingService>();

        services.AddHealthChecks().AddCheck<LangfuseHealthCheck>(LangfuseHealthCheck.Name, failureStatus: HealthStatus.Degraded);

        return builder;
    }

    // Short timeouts and quick retries: prompt fetches sit on the chat path and fall back to cached or local prompts.
    internal static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(250);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        // A retried POST could make seeding create duplicate prompt versions.
        options.Retry.DisableForUnsafeHttpMethods();

        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);
    }

    private sealed class PromptsModuleMarker;
}
