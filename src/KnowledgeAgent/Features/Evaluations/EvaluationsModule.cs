using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace KnowledgeAgent.Features.Evaluations;

public static class EvaluationsModule
{
    public static IHostApplicationBuilder AddEvaluations(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EvaluationModuleMarker)))
        {
            return builder;
        }

        services.AddSingleton<EvaluationModuleMarker>();
        services.AddOptions<EvaluationOptions>()
            .BindConfiguration(EvaluationOptions.SectionName)
            .Validate(options => options.Parallelism is >= 1 and <= 32, "Evaluation:Parallelism must be between 1 and 32.")
            .Validate(options => options.MaxItemsPerRun >= 1, "Evaluation:MaxItemsPerRun must be at least 1.")
            .ValidateOnStart();
        services.AddOptions<LangfuseOptions>().BindConfiguration(LangfuseOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient<LangfuseEvalClient>((serviceProvider, client) =>
                LangfuseEvalClient.ConfigureHttpClient(client, serviceProvider.GetRequiredService<IOptions<LangfuseOptions>>().Value))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(ConfigureResilience);

        services.TryAddSingleton(serviceProvider => new LangfuseEvalLinks(
            serviceProvider.GetRequiredService<IOptions<LangfuseOptions>>().Value,
            serviceProvider.GetRequiredService<LangfuseEvalClient>(),
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<LangfuseEvalLinks>()));

        services.TryAddSingleton<BuiltinDatasetCatalog>();
        services.TryAddSingleton<EvalRunQueue>();
        services.TryAddSingleton<EvalRunExecutor>();
        services.AddJob<EvalRunJob>(EvalRunQueue.JobKind, new JobPolicy { MaxConcurrency = 1, MaxAttempts = 2 });
        services.TryAddScoped<IEvalRunRepository, EvalRunRepository>();
        services.TryAddScoped<IEvalService, EvalService>();
        services.TryAddScoped<ICustomDatasetRepository, CustomDatasetRepository>();
        services.TryAddScoped<ICustomDatasetService, CustomDatasetService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, EvalRunRecoveryInitializer>());
        return builder;
    }

    internal static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        // Creating a dataset is not idempotent, so unsafe methods are never retried.
        options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    }

    private sealed class EvaluationModuleMarker;
}
