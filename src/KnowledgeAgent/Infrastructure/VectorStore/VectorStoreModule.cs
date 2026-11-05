using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using KnowledgeAgent.Infrastructure.VectorStore.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Infrastructure.VectorStore;

public static class VectorStoreModule
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(10);

    public static IHostApplicationBuilder AddVectorStore(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(VectorStoreRegistrationMarker)))
        {
            return builder;
        }

        services.AddSingleton(new VectorStoreRegistrationMarker());

        services.AddOptions<VectorStoreOptions>()
            .Bind(builder.Configuration.GetSection(VectorStoreOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<VectorStoreOptions>, VectorStoreOptionsValidator>());

        services.TryAddSingleton<InMemoryVectorStore>();
        services.TryAddSingleton<ClickHouseVectorStore>(CreateClickHouseStore);
        services.TryAddSingleton<IVectorStore>(ResolveVectorStore);

        services.AddSingleton<IStartupInitializer, VectorStoreSchemaInitializer>();
        services.AddHealthChecks().AddCheck<VectorStoreHealthCheck>(
            VectorStoreHealthCheck.Name,
            failureStatus: null,
            tags: ["ready"],
            timeout: HealthCheckTimeout);

        return builder;
    }

    // Chosen on first resolve rather than at registration, so configuration added after AddVectorStore is honoured.
    private static IVectorStore ResolveVectorStore(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<VectorStoreOptions>>().Value;
        return VectorStoreProviders.Normalize(options.Provider) switch
        {
            VectorStoreProviders.ClickHouse => services.GetRequiredService<ClickHouseVectorStore>(),
            VectorStoreProviders.InMemory => services.GetRequiredService<InMemoryVectorStore>(),
            _ => throw new InvalidOperationException(VectorStoreProviders.UnsupportedMessage(options.Provider)),
        };
    }

    private static ClickHouseVectorStore CreateClickHouseStore(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<VectorStoreOptions>>().Value;
        var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"VectorStore:Provider is ClickHouse but the connection string 'ConnectionStrings:{options.ConnectionStringName}' " +
                "is not configured (expected Host=...;Port=8123;Username=...;Password=...).");
        }

        return new ClickHouseVectorStore(
            connectionString,
            options.Database,
            options.Table,
            services.GetRequiredService<ILogger<ClickHouseVectorStore>>());
    }

    private sealed class VectorStoreRegistrationMarker;
}
