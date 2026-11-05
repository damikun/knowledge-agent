using System.Text.Json;
using Inngest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace KnowledgeAgent.Infrastructure.Jobs;

// One Inngest function per job kind, triggered by one event per job, plus a cron function per enabled schedule.
internal sealed class InngestJobEngine(IInngestBuilder inngest, JobsOptions settings)
{
    public const string EndpointPath = "/inngest";
    public const string CloudDashboardUrl = "https://app.inngest.com";
    public const string DefaultDevServerUrl = "http://localhost:8288";

    private const string KeyExpression = "event.data.key";
    private static readonly TimeSpan MinDebounce = TimeSpan.FromSeconds(1);

    public static string EventName(string kind) => $"jobs/{kind}";

    public static string FunctionId(string kind) => kind.Replace('.', '-');

    public static void Add(IHostApplicationBuilder builder, JobsOptions settings)
    {
        var services = builder.Services;
        var inngest = services
            .AddInngest(builder.Configuration, options => options.AppId = settings.Inngest.AppId)
            .AddInstrumentation();
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInngestInstrumentation());
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInngestInstrumentation());

        var engine = new InngestJobEngine(inngest, settings);
        services.AddSingleton(engine);
        services.TryAddSingleton<IJobQueue, InngestJobQueue>();

        // Jobs and schedules registered before the engine get their functions here, later ones as they are registered.
        var registered = services.Select(descriptor => descriptor.ImplementationInstance).ToList();
        foreach (var registration in registered.OfType<JobRegistration>())
        {
            engine.AddFunction(registration);
        }

        foreach (var schedule in registered.OfType<JobScheduleRegistration>())
        {
            engine.AddSchedule(schedule);
        }
    }

    public static InngestJobEngine? Find(IServiceCollection services) =>
        services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(InngestJobEngine))?.ImplementationInstance as InngestJobEngine;

    public static string ResolveDashboardUrl(InngestJobsOptions settings, IConfiguration configuration)
    {
        if (settings.DashboardUrl is { Length: > 0 } configured)
        {
            return configured;
        }

        // The SDK reads these words as flags and anything else as the dev server's address.
        var dev = configuration["INNGEST_DEV"]?.Trim() ?? "";
        var flag = dev.ToLowerInvariant();
        if (flag is not ("" or "0" or "false" or "off" or "no"))
        {
            return flag is "1" or "true" or "on" or "yes" ? DefaultDevServerUrl : Origin(dev);
        }

        return configuration["INNGEST_BASE_URL"] is { Length: > 0 } baseUrl ? Origin(baseUrl) : CloudDashboardUrl;
    }

    public void AddFunction(JobRegistration registration) =>
        inngest.AddFunction<JobEvent>(
            CreateOptions(registration),
            (context, cancellationToken) => RunAsync(registration, context, cancellationToken),
            registration.HandlesFailure ? (context, cancellationToken) => OnFailureAsync(registration, context, cancellationToken) : null);

    // The cron expression and the enabled flag are read here, so changing a schedule takes a restart.
    public void AddSchedule(JobScheduleRegistration registration)
    {
        var schedule = JobSchedules.Resolve(registration, settings);
        if (schedule.Enabled)
        {
            inngest.AddFunction<JsonElement>(CreateScheduleOptions(schedule), (context, _) => QueueScheduledJobAsync(schedule, context));
        }
    }

    // JobPolicy as Inngest flow control; must match what InProcessJobRunner does.
    internal static FunctionOptions CreateOptions(JobRegistration registration)
    {
        var policy = registration.Policy;
        return new FunctionOptions
        {
            Id = FunctionId(registration.Kind),
            Name = registration.Kind,
            Triggers = [Trigger.Event(EventName(registration.Kind))],
            // One run per key at a time, so a source is never ingested twice at once.
            Concurrency = [new ConcurrencyLimit(Math.Max(1, policy.MaxConcurrency)), new ConcurrencyLimit(1) { Key = KeyExpression }],
            Retries = Math.Max(0, policy.MaxAttempts - 1),
            // A key starts one run per 24 hours; Inngest drops later events for it.
            Idempotency = policy.Idempotent ? KeyExpression : null,
            // An event for a key that runs starts no run.
            Singleton = policy.Overlap == JobOverlap.Skip ? new Singleton(SingletonMode.Skip) { Key = KeyExpression } : null,
            // Inngest's shortest debounce period is one second.
            Debounce = policy.Debounce is { } period && period > TimeSpan.Zero
                ? new Debounce(period < MinDebounce ? MinDebounce : period) { Key = KeyExpression }
                : null,
        };
    }

    internal static FunctionOptions CreateScheduleOptions(EffectiveSchedule schedule) => new()
    {
        Id = $"schedule-{schedule.Name}",
        Name = schedule.Name,
        Triggers = [Trigger.Cron(schedule.Cron)],
        Concurrency = [new ConcurrencyLimit(1)],
    };

    // The cron run only queues the job, so it runs in its kind's function with that function's limits, like Run now.
    private static async Task<object?> QueueScheduledJobAsync(EffectiveSchedule schedule, FunctionContext<JsonElement> context)
    {
        await context.Step.SendEventAsync("queue-job", new InngestEvent(EventName(schedule.Kind), new JobEvent(schedule.Key)));
        return null;
    }

    private static async Task<object?> RunAsync(JobRegistration registration, FunctionContext<JobEvent> context, CancellationToken cancellationToken)
    {
        var key = context.Event.Data?.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new NonRetriableException($"The {context.EventName} event has no job key.");
        }

        var handler = (IJobHandler)context.Services.GetRequiredService(registration.HandlerType);
        var job = new JobContext(context.RunId, registration.Kind, key, context.Attempt + 1, context.MaxAttempts ?? registration.Policy.MaxAttempts)
        {
            Steps = new InngestJobSteps(context.Step),
        };
        await handler.HandleAsync(job, cancellationToken);
        return null;
    }

    // Inngest runs this once the run has failed for good, as a function of its own triggered by inngest/function.failed;
    // the failed run's event carries the key.
    private static async Task OnFailureAsync(JobRegistration registration, FunctionContext<FunctionFailure> context, CancellationToken cancellationToken)
    {
        var failure = context.Event.Data;
        if (failure is null || ReadKey(failure.Event) is not { Length: > 0 } key)
        {
            return;
        }

        var handler = (IJobFailureHandler)context.Services.GetRequiredService(registration.HandlerType);
        var attempts = Math.Max(1, registration.Policy.MaxAttempts);
        await handler.OnFailureAsync(new JobFailure(failure.RunId, registration.Kind, key, attempts, failure.Error.Message), cancellationToken);
    }

    private static string? ReadKey(JsonElement @event) =>
        @event.ValueKind == JsonValueKind.Object
        && @event.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null;

    private static string Origin(string address) =>
        Uri.TryCreate(address.Contains("://", StringComparison.Ordinal) ? address : $"http://{address}", UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : address;
}

internal sealed record JobEvent(string Key);

// Inngest memoizes each step by its id and retries it on its own; a step that failed for good surfaces as
// StepFailedException at its call site.
internal sealed class InngestJobSteps(IStepTools step) : IJobSteps
{
    public async Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> handler)
    {
        try
        {
            return await step.RunAsync(id, handler);
        }
        catch (StepFailedException exception)
        {
            throw new JobStepFailedException(id, exception.Message, exception);
        }
    }
}
