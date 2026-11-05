using KnowledgeAgent.Features.Analytics;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KnowledgeAgent.Tests.Infrastructure.Persistence.Support;

public static class PersistenceHost
{
    public static IHost Build(
        IReadOnlyDictionary<string, string?> settings,
        TimeProvider? timeProvider = null,
        Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        }));

        if (timeProvider is not null)
        {
            builder.Services.AddSingleton<TimeProvider>(timeProvider);
        }

        configure?.Invoke(builder);
        builder.AddPersistence();
        builder.AddJobs();
        builder.Services.AddScoped<ISourceRepository, SourceRepository>();
        builder.Services.AddScoped<ISourceFolderRepository, SourceFolderRepository>();
        builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
        builder.Services.AddScoped<IHandoverRepository, HandoverRepository>();
        builder.Services.AddScoped<IModelProfileOverrideStore, ModelProfileOverrideStore>();
        builder.Services.AddScoped<IProviderSettingsStore, ProviderSettingsStore>();
        builder.Services.AddScoped<IEvalRunRepository, EvalRunRepository>();
        builder.Services.AddScoped<ICustomDatasetRepository, CustomDatasetRepository>();
        builder.Services.AddScoped<IConversationSearch, ConversationSearchRepository>();
        builder.Services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        builder.Services.AddScoped<IPeopleRepository, PeopleRepository>();
        builder.Services.AddScoped<ISegmentService, SegmentService>();
        builder.Services.AddSingleton<SegmentCountCache>();
        builder.Services.AddScoped<ILabelRepository, LabelRepository>();
        builder.Services.AddScoped<IConversationCounts, ConversationCountRepository>();
        builder.Services.AddScoped<IConversationReads, ConversationReadRepository>();
        builder.Services.AddScoped<IStaffDirectory, StaffDirectory>();
        return builder.Build();
    }

    public static async Task RunStartupInitializersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var initializer in scope.ServiceProvider.GetServices<IStartupInitializer>().OrderBy(initializer => initializer.Order))
        {
            await initializer.InitializeAsync(cancellationToken);
        }
    }
}

public abstract class PersistenceFixture : IAsyncLifetime
{
    private IHost? host;

    // Starts after the real "now" so bumped timestamps always move forward.
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public virtual IServiceProvider Services =>
        host?.Services ?? throw new InvalidOperationException("The persistence fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        var settings = await StartDatabaseAsync();
        if (settings is null)
        {
            return;
        }

        host = PersistenceHost.Build(settings, Time);
        await PersistenceHost.RunStartupInitializersAsync(host.Services, CancellationToken.None);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host?.Dispose();
        }
    }

    /// <summary>Returns the host configuration, or null when the database cannot be provided.</summary>
    protected abstract Task<IReadOnlyDictionary<string, string?>?> StartDatabaseAsync();
}

public sealed class InMemoryPersistenceFixture : PersistenceFixture
{
    protected override Task<IReadOnlyDictionary<string, string?>?> StartDatabaseAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, string?>?>(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["Persistence:InMemoryDatabaseName"] = $"persistence-tests-{Guid.NewGuid():N}",
        });
}

public sealed class PostgresPersistenceFixture : PersistenceFixture
{
    private PostgreSqlContainer? container;
    private string connectionString = string.Empty;

    // Every entry point skips the calling test when Docker is unavailable.
    public override IServiceProvider Services
    {
        get
        {
            Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
            return base.Services;
        }
    }

    public string ConnectionString
    {
        get
        {
            Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
            return connectionString;
        }
    }

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public static IReadOnlyDictionary<string, string?> PostgresSettings(string connectionString) => new Dictionary<string, string?>
    {
        ["Persistence:Provider"] = "Postgres",
        ["Persistence:ConnectionStringName"] = "knowledgedb",
        ["ConnectionStrings:knowledgedb"] = connectionString,
    };

    protected override async Task<IReadOnlyDictionary<string, string?>?> StartDatabaseAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return null;
        }

        container = new PostgreSqlBuilder(ContainerImages.Postgres).WithDatabase("knowledge").Build();
        await container.StartAsync();
        connectionString = container.GetConnectionString();
        return PostgresSettings(connectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
