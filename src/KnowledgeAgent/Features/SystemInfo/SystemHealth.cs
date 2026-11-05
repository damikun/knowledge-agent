using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Alerts;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Security;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.SystemInfo;

public enum SystemComponentStatus
{
    Healthy,
    Degraded,
    Down,
    NotConfigured,
    Unknown,
}

// Detail is written here, never copied from an exception, so no host, credential or connection string leaks.
public sealed record SystemComponentHealthDto(string Key, SystemComponentStatus Status, string Detail, DateTimeOffset CheckedAt, string? Variant = null);

public sealed record SystemHealthDto(IReadOnlyList<SystemComponentHealthDto> Components);

public static class SystemComponents
{
    public const string Api = "api";
    public const string Database = "database";
    public const string VectorStore = "vectorStore";
    public const string Jobs = "jobs";
    public const string SignIn = "signIn";
    public const string Langfuse = "langfuse";
    public const string Models = "models";
    public const string Email = "email";
    public const string Slack = "slack";
}

// The page may be open in several tabs, so one result serves everyone for a short while.
internal sealed partial class SystemHealthService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<SystemHealthService> logger) : IDisposable
{
    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);

    private const string DatabaseCheck = nameof(KnowledgeDbContext);
    private static readonly HashSet<string> Checks = new(StringComparer.Ordinal)
    {
        DatabaseCheck, VectorStoreHealthCheck.Name, JobsModule.InngestHealthCheckName, LangfuseHealthCheck.Name,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    // Swapped whole, so a reader outside the gate never sees a result with another result's time.
    private Cached? _cached;

    public async Task<SystemHealthDto> GetAsync(CancellationToken cancellationToken)
    {
        if (Fresh() is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh() is { } again)
            {
                return again;
            }

            var health = await CheckAsync(cancellationToken);
            Volatile.Write(ref _cached, new Cached(health, timeProvider.GetUtcNow()));
            return health;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private SystemHealthDto? Fresh() =>
        Volatile.Read(ref _cached) is { } cached && timeProvider.GetUtcNow() - cached.At < CacheFor ? cached.Health : null;

    private sealed record Cached(SystemHealthDto Health, DateTimeOffset At);

    private async Task<SystemHealthDto> CheckAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var configuration = services.GetRequiredService<IConfiguration>();

        var reports = await RunHealthChecksAsync(services, cancellationToken);
        var signIn = await CheckSignInAsync(services, configuration, cancellationToken);
        var alerts = await AdminAlertsAsync(services, cancellationToken);
        var now = timeProvider.GetUtcNow();

        var components = new List<SystemComponentHealthDto>
        {
            new(SystemComponents.Api, SystemComponentStatus.Healthy, "Answering requests", now),
            Database(configuration, reports, now),
            VectorIndex(services.GetRequiredService<IVectorStore>(), reports, now),
            Jobs(services.GetRequiredService<JobEngineInfo>(), reports, now),
            signIn with { CheckedAt = now },
            Langfuse(configuration, reports, now),
            await ModelsAsync(services, alerts, now, cancellationToken),
        };
        components.AddRange(await IntegrationsAsync(services, alerts, now, cancellationToken));
        return new SystemHealthDto(components);
    }

    private async Task<IReadOnlyDictionary<string, HealthReportEntry>?> RunHealthChecksAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<HealthCheckService>() is not { } health)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            var report = await health.CheckHealthAsync(registration => Checks.Contains(registration.Name), timeout.Token);
            return report.Entries;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogChecksTimedOut(logger, CheckTimeout.TotalSeconds);
            return null;
        }
    }

    private static SystemComponentHealthDto FromCheck(
        string key,
        IReadOnlyDictionary<string, HealthReportEntry>? reports,
        string check,
        DateTimeOffset now,
        string? variant,
        string healthy,
        string degraded,
        string down)
    {
        if (reports is null)
        {
            return new(key, SystemComponentStatus.Unknown, "The check did not finish in time", now, variant);
        }

        if (!reports.TryGetValue(check, out var entry))
        {
            return new(key, SystemComponentStatus.Unknown, "Not checked by this installation", now, variant);
        }

        return entry.Status switch
        {
            HealthStatus.Healthy => new(key, SystemComponentStatus.Healthy, healthy, now, variant),
            HealthStatus.Degraded => new(key, SystemComponentStatus.Degraded, degraded, now, variant),
            _ => new(key, SystemComponentStatus.Down, down, now, variant),
        };
    }

    private static SystemComponentHealthDto Database(IConfiguration configuration, IReadOnlyDictionary<string, HealthReportEntry>? reports, DateTimeOffset now)
    {
        var provider = configuration.GetSection(PersistenceOptions.SectionName).Get<PersistenceOptions>()?.Provider ?? "Postgres";
        if (string.Equals(provider, PersistenceModule.InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            return FromCheck(SystemComponents.Database, reports, DatabaseCheck, now, "inmemory",
                "In memory: everything is lost when the API restarts", "In memory and slow to answer", "The in-memory store does not answer");
        }

        return FromCheck(SystemComponents.Database, reports, DatabaseCheck, now, "postgres",
            "Connected", "Connected but slow to answer", "The API cannot reach the database");
    }

    private static SystemComponentHealthDto VectorIndex(IVectorStore store, IReadOnlyDictionary<string, HealthReportEntry>? reports, DateTimeOffset now)
    {
        var inMemory = string.Equals(store.ProviderName, VectorStoreProviders.InMemory, StringComparison.OrdinalIgnoreCase);
        return inMemory
            ? FromCheck(SystemComponents.VectorStore, reports, VectorStoreHealthCheck.Name, now, "inmemory",
                "In memory: sources must be indexed again after a restart", "In memory and slow to answer", "The in-memory index does not answer")
            : FromCheck(SystemComponents.VectorStore, reports, VectorStoreHealthCheck.Name, now, "clickhouse",
                "Search index reachable", "Search index slow to answer", "The API cannot reach the search index");
    }

    private static SystemComponentHealthDto Jobs(JobEngineInfo engine, IReadOnlyDictionary<string, HealthReportEntry>? reports, DateTimeOffset now) =>
        engine.KeepsJobsAcrossRestarts
            ? FromCheck(SystemComponents.Jobs, reports, JobsModule.InngestHealthCheckName, now, "inngest",
                "Connected; jobs survive restarts", "Not reachable; chat works, background jobs wait", "Not reachable; background jobs wait")
            : new(SystemComponents.Jobs, SystemComponentStatus.Healthy, "Jobs run inside the API; queued work is lost on restart", now, "inprocess");

    private static SystemComponentHealthDto Langfuse(IConfiguration configuration, IReadOnlyDictionary<string, HealthReportEntry>? reports, DateTimeOffset now)
    {
        var options = configuration.GetSection(LangfuseOptions.SectionName).Get<LangfuseOptions>() ?? new LangfuseOptions();
        if (!options.IsConfigured)
        {
            return new(SystemComponents.Langfuse, SystemComponentStatus.NotConfigured, "Not set up; prompts come from the built-in copies and no traces are kept", now);
        }

        var component = FromCheck(SystemComponents.Langfuse, reports, LangfuseHealthCheck.Name, now, null,
            "Reachable", "Not answering; prompts come from the cache", "Not answering");
        if (component.Status == SystemComponentStatus.Healthy
            && reports is not null
            && reports.TryGetValue(LangfuseHealthCheck.Name, out var entry)
            && entry.Data?.TryGetValue("version", out var version) == true
            && version is string text && text.Length is > 0 and < 40 && text != "unknown")
        {
            return component with { Detail = $"Reachable, version {text}" };
        }

        return component;
    }

    private async Task<SystemComponentHealthDto> CheckSignInAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (auth.Mode == AuthMode.None)
        {
            return new(SystemComponents.SignIn, SystemComponentStatus.NotConfigured, "Sign-in is off: everyone is an admin (development only)", default, "none");
        }

        var oidc = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthSchemes.Oidc);
        var metadata = oidc.MetadataAddress.TrimToNull()
            ?? (oidc.Authority.TrimToNull() is { } authority ? $"{authority.TrimEnd('/')}/.well-known/openid-configuration" : null);
        if (metadata is null || oidc.Backchannel is null)
        {
            return new(SystemComponents.SignIn, SystemComponentStatus.Unknown, "Not checked", default, "oidc");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryTimeout);
        try
        {
            using var response = await oidc.Backchannel.GetAsync(metadata, timeout.Token);
            return response.IsSuccessStatusCode
                ? new(SystemComponents.SignIn, SystemComponentStatus.Healthy, "Reachable; staff can sign in", default, "oidc")
                : new(SystemComponents.SignIn, SystemComponentStatus.Down, $"Answered {(int)response.StatusCode}; new sign-ins fail", default, "oidc");
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            LogSignInUnreachable(logger, ex);
            return new(SystemComponents.SignIn, SystemComponentStatus.Down, "Not reachable; new sign-ins fail, open sessions keep working", default, "oidc");
        }
    }

    private async Task<IReadOnlyList<Alert>?> AdminAlertsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            return await services.GetRequiredService<AlertEvaluator>().EvaluateAsync(AlertAudience.Admin, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAlertsFailed(logger, ex);
            return null;
        }
    }

    private static async Task<SystemComponentHealthDto> ModelsAsync(IServiceProvider services, IReadOnlyList<Alert>? alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var profiles = await services.GetRequiredService<IModelProfileResolver>().ListAsync(cancellationToken);
        var chat = profiles.FirstOrDefault(profile => string.Equals(profile.Name, AgentDefaults.ProfileName, StringComparison.OrdinalIgnoreCase));
        var embedding = profiles.FirstOrDefault(profile => string.Equals(profile.Name, EmbeddingService.ProfileName, StringComparison.OrdinalIgnoreCase))
            ?? profiles.FirstOrDefault(profile => profile.Kind == ModelKind.Embedding);
        var rejected = alerts?.Any(alert => alert.Kind == AlertKinds.ProviderRejected) == true;

        static string Describe(ResolvedModelProfile? model) => model is null ? "not connected" : $"{model.ProviderKey}, {model.Model}";
        var summary = $"Answers: {Describe(chat?.Effective)}. Embeddings: {Describe(embedding?.Effective)}.";

        if (chat?.Effective is null)
        {
            return new(SystemComponents.Models, SystemComponentStatus.Down, $"No answering model, so the agent cannot answer. {summary}", now);
        }

        if (embedding?.Effective is null)
        {
            return new(SystemComponents.Models, SystemComponentStatus.Degraded, $"No embedding model, so sources cannot be indexed. {summary}", now);
        }

        return rejected
            ? new(SystemComponents.Models, SystemComponentStatus.Degraded, $"A provider rejected its key. {summary}", now)
            : new(SystemComponents.Models, SystemComponentStatus.Healthy, summary, now);
    }

    private static async Task<SystemComponentHealthDto[]> IntegrationsAsync(IServiceProvider services, IReadOnlyList<Alert>? alerts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var integrations = await services.GetRequiredService<IntegrationService>().GetAsync(cancellationToken);
        bool Failing(string kind) => alerts?.Any(alert => alert.Kind == kind) == true;

        var mail = integrations.Email;
        var email = !mail.CanSend
            ? new SystemComponentHealthDto(SystemComponents.Email, SystemComponentStatus.NotConfigured, "Not set up; requests for a person stay in the Inbox", now)
            : Failing(AlertKinds.EmailFailing)
                ? new(SystemComponents.Email, SystemComponentStatus.Degraded, "Recent requests could not be emailed", now)
                : new(SystemComponents.Email, SystemComponentStatus.Healthy, mail.CanReceive ? "Sends requests and reads replies" : "Sends requests; replies are not read", now);

        var chat = integrations.Slack;
        var slack = !chat.CanPost
            ? new SystemComponentHealthDto(SystemComponents.Slack, SystemComponentStatus.NotConfigured, "Not set up; requests for a person stay in the Inbox", now)
            : chat.CanReceive && !chat.Connected
                ? new(SystemComponents.Slack, SystemComponentStatus.Degraded, "Posts requests, but replies from Slack do not arrive right now", now)
                : Failing(AlertKinds.SlackFailing)
                    ? new(SystemComponents.Slack, SystemComponentStatus.Degraded, "Recent requests could not be posted", now)
                    : new(SystemComponents.Slack, SystemComponentStatus.Healthy, chat.CanReceive ? "Posts requests and receives replies" : "Posts requests; replies are not received", now);

        return [email, slack];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system health checks did not finish within {Seconds} s.")]
    private static partial void LogChecksTimedOut(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "The identity provider's metadata could not be read for the system health.")]
    private static partial void LogSignInUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerts could not be evaluated for the system health.")]
    private static partial void LogAlertsFailed(ILogger logger, Exception exception);
}
