using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence;

public sealed class PersistenceModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InMemory_provider_registers_the_context_initializer_and_health_check()
    {
        using var host = PersistenceHost.Build(new Dictionary<string, string?> { ["Persistence:Provider"] = "InMemory" });

        await PersistenceHost.RunStartupInitializersAsync(host.Services, Ct);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<KnowledgeDbContext>().Database.IsInMemory().ShouldBeTrue();
        var initializer = services.GetServices<IStartupInitializer>().ShouldHaveSingleItem();
        initializer.Order.ShouldBe(StartupOrder.Persistence);
        initializer.IsCritical.ShouldBeTrue();

        var sources = services.GetRequiredService<ISourceRepository>();
        var source = await sources.AddAsync(new Source { Type = SourceType.Text, Title = "hello" }, cancellationToken: Ct);
        (await sources.GetAsync(source.Id, Ct)).ShouldNotBeNull();

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        report.Entries.ShouldContainKey(nameof(KnowledgeDbContext));
        report.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Each_host_gets_its_own_InMemory_store_and_the_provider_name_is_case_insensitive()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "inmemory",
            ["Persistence:InMemoryDatabaseName"] = "same-name-in-both-hosts",
        };
        using var first = PersistenceHost.Build(settings);
        using var second = PersistenceHost.Build(settings);
        var source = new Source { Type = SourceType.Text, Title = "only in the first host" };

        await InScopeAsync(first, sources => sources.AddAsync(source, cancellationToken: Ct));

        (await InScopeAsync(first, sources => sources.GetAsync(source.Id, Ct))).ShouldNotBeNull();
        (await InScopeAsync(second, sources => sources.GetAsync(source.Id, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Per_host_InMemory_stores_do_not_cost_an_EF_service_provider_each()
    {
        // EF warns once it has created more than 20 internal service providers.
        var logs = new FakeLogCollector();
        for (var i = 0; i < 25; i++)
        {
            using var host = PersistenceHost.Build(
                new Dictionary<string, string?> { ["Persistence:Provider"] = "InMemory" },
                configure: builder => builder.Logging.AddProvider(new FakeLoggerProvider(logs)));
            await PersistenceHost.RunStartupInitializersAsync(host.Services, Ct);
            (await InScopeAsync(host, sources => sources.ListAsync(Ct))).ShouldBeEmpty();
        }

        logs.GetSnapshot().ShouldNotContain(record => record.Id.Id == CoreEventId.ManyServiceProvidersCreatedWarning.Id);
    }

    [Fact]
    public void An_unknown_provider_is_rejected()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            PersistenceHost.Build(new Dictionary<string, string?> { ["Persistence:Provider"] = "Sqlite" }));

        exception.Message.ShouldContain("'Sqlite'");
        exception.Message.ShouldContain("Postgres, InMemory");
    }

    [Fact]
    public void Postgres_without_a_connection_string_is_rejected()
    {
        var exception = Should.Throw<InvalidOperationException>(() => PersistenceHost.Build(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "Postgres",
            ["Persistence:ConnectionStringName"] = "missingdb",
        }));

        exception.Message.ShouldContain("ConnectionStrings:missingdb");
    }

    [Fact]
    public async Task Postgres_provider_is_enriched_by_Aspire()
    {
        // Resolving the context and its execution strategy never opens a connection.
        using var host = PersistenceHost.Build(PostgresPersistenceFixture.PostgresSettings(UnreachableConnectionString()));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();

        db.Database.IsNpgsql().ShouldBeTrue();
        db.Database.CreateExecutionStrategy().ShouldBeOfType<NpgsqlRetryingExecutionStrategy>();
        host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Select(registration => registration.Name)
            .ShouldContain(nameof(KnowledgeDbContext));
    }

    [Fact]
    public async Task Postgres_initializer_does_nothing_when_MigrateOnStartup_is_off()
    {
        var settings = new Dictionary<string, string?>(PostgresPersistenceFixture.PostgresSettings(UnreachableConnectionString()))
        {
            ["Persistence:MigrateOnStartup"] = "false",
        };
        using var host = PersistenceHost.Build(settings);

        // Would throw if it tried to reach the closed port.
        await PersistenceHost.RunStartupInitializersAsync(host.Services, Ct);
    }

    [Fact]
    public async Task Initializer_retries_transient_failures_with_backoff_until_the_budget_is_spent()
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>().UseNpgsql(UnreachableConnectionString()).Options;
        await using var db = new KnowledgeDbContext(options);
        var logger = new FakeLogger<PersistenceStartupInitializer>();
        var initializer = new PersistenceStartupInitializer(db, Options.Create(new PersistenceOptions()), logger)
        {
            RetryBudget = TimeSpan.FromSeconds(2.5),
        };
        var stopwatch = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => initializer.InitializeAsync(Ct));

        exception.ShouldNotBeNull();
        PersistenceStartupInitializer.IsTransient(exception).ShouldBeTrue();
        logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Warning).ShouldBeGreaterThanOrEqualTo(1);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Transient_failures_are_told_apart_from_permanent_ones()
    {
        PersistenceStartupInitializer.IsTransient(
            new NpgsqlException("connect failed", new SocketException((int)SocketError.ConnectionRefused))).ShouldBeTrue();
        PersistenceStartupInitializer.IsTransient(
            new PostgresException("the database system is starting up", "FATAL", "FATAL", PostgresErrorCodes.CannotConnectNow)).ShouldBeTrue();
        PersistenceStartupInitializer.IsTransient(
            new InvalidOperationException("transient failure", new TimeoutException())).ShouldBeTrue();
        PersistenceStartupInitializer.IsTransient(
            new PostgresException("password authentication failed", "FATAL", "FATAL", PostgresErrorCodes.InvalidPassword)).ShouldBeFalse();
        PersistenceStartupInitializer.IsTransient(
            new InvalidOperationException("The model has pending changes.")).ShouldBeFalse();
    }

    private static async Task<T> InScopeAsync<T>(IHost host, Func<ISourceRepository, Task<T>> action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISourceRepository>());
    }

    private static string UnreachableConnectionString()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"Host=127.0.0.1;Port={port};Database=knowledge;Username=postgres;Password=postgres;Timeout=3";
    }
}
