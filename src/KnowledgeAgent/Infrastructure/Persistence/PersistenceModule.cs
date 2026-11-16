using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace KnowledgeAgent.Infrastructure.Persistence;

public static class PersistenceModule
{
    public const string PostgresProvider = "Postgres";
    public const string InMemoryProvider = "InMemory";

    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(PersistenceOptions.SectionName);
        var options = section.Get<PersistenceOptions>() ?? new PersistenceOptions();
        builder.Services.AddOptions<PersistenceOptions>().Bind(section);

        if (string.Equals(options.Provider, PostgresProvider, StringComparison.OrdinalIgnoreCase))
        {
            AddPostgres(builder, options);
        }
        else if (string.Equals(options.Provider, InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            AddInMemory(builder, options);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown {PersistenceOptions.SectionName}:Provider '{options.Provider}'. Supported values: {PostgresProvider}, {InMemoryProvider}.");
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<DbContextGate>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupInitializer, PersistenceStartupInitializer>());

        builder.Services.AddDataProtection()
            .SetApplicationName("KnowledgeAgent")
            .PersistKeysToDbContext<KnowledgeDbContext>();

        return builder;
    }

    private static void AddPostgres(IHostApplicationBuilder builder, PersistenceOptions options)
    {
        var connectionString = builder.Configuration.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{PersistenceOptions.SectionName}:Provider is '{PostgresProvider}' but connection string " +
                $"'ConnectionStrings:{options.ConnectionStringName}' is not configured " +
                $"(set ConnectionStrings__{options.ConnectionStringName}, or use {PersistenceOptions.SectionName}:Provider={InMemoryProvider}).");
        }

        builder.Services.AddDbContextPool<KnowledgeDbContext>(dbContext => dbContext.UseNpgsql(connectionString));

        builder.Services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        builder.EnrichNpgsqlDbContext<KnowledgeDbContext>();
    }

    private static void AddInMemory(IHostApplicationBuilder builder, PersistenceOptions options)
    {
        builder.Services.TryAddSingleton<InMemoryDatabaseRoot>();
        builder.Services.AddDbContextPool<KnowledgeDbContext>((services, dbContext) =>
            dbContext.UseInMemoryDatabase(options.InMemoryDatabaseName, services.GetRequiredService<InMemoryDatabaseRoot>()));

        builder.Services.AddHealthChecks().AddDbContextCheck<KnowledgeDbContext>();
    }
}
