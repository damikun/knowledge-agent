using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using KnowledgeAgent.Infrastructure.VectorStore.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore;

public sealed class VectorStoreModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InMemoryProvider_RegistersTheStore_TheStartupInitializer_AndTheHealthCheck()
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = "InMemory" });

        var store = host.Services.GetRequiredService<IVectorStore>();
        store.ShouldBeOfType<InMemoryVectorStore>();
        store.ProviderName.ShouldBe("InMemory");
        host.Services.GetRequiredService<IVectorStore>().ShouldBeSameAs(store);

        var initializer = host.Services.GetServices<IStartupInitializer>().ShouldHaveSingleItem();
        initializer.Order.ShouldBe(StartupOrder.VectorStore);
        initializer.IsCritical.ShouldBeTrue();
        await initializer.InitializeAsync(Ct);

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        report.Entries.TryGetValue("vectorstore", out var entry).ShouldBeTrue();
        entry.Status.ShouldBe(HealthStatus.Healthy);
        entry.Tags.ShouldContain("ready");
    }

    [Theory]
    [InlineData("inmemory")]
    [InlineData(" INMEMORY ")]
    public void ProviderNames_AreCaseInsensitive(string provider)
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = provider });

        host.Services.GetRequiredService<IVectorStore>().ShouldBeOfType<InMemoryVectorStore>();
    }

    [Fact]
    public void UnknownProvider_FailsWithAClearMessage()
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = "Qdrant" });

        var exception = Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IVectorStore>());

        exception.Message.ShouldContain("VectorStore:Provider 'Qdrant' is not supported. Supported providers: ClickHouse, InMemory.");
    }

    [Fact]
    public async Task UnknownProvider_FailsHostStartup()
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = "Qdrant" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(Ct));

        exception.Message.ShouldContain("Qdrant");
    }

    [Fact]
    public void ClickHouseProvider_IsBuiltFromConfiguration()
    {
        using var host = BuildHost(new()
        {
            ["VectorStore:Provider"] = "ClickHouse",
            ["VectorStore:ConnectionStringName"] = "vectors",
            ["VectorStore:Database"] = "kb",
            ["VectorStore:Table"] = "embeddings",
            ["ConnectionStrings:vectors"] = "Host=clickhouse.internal;Port=8124;Username=agent;Password=secret;Database=other",
        });

        var store = host.Services.GetRequiredService<IVectorStore>().ShouldBeOfType<ClickHouseVectorStore>();

        store.ProviderName.ShouldBe("ClickHouse");
        store.Database.ShouldBe("kb");
        store.Table.ShouldBe("embeddings");
        store.Settings.Host.ShouldBe("clickhouse.internal");
        store.Settings.Port.ShouldBe((ushort)8124);
        store.Settings.Username.ShouldBe("agent");
        store.Settings.Database.ShouldBeEmpty();
    }

    [Fact]
    public void ClickHouseProvider_WithoutConnectionString_FailsWithAClearMessage()
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = "ClickHouse" });

        var exception = Should.Throw<InvalidOperationException>(() => host.Services.GetRequiredService<IVectorStore>());

        exception.Message.ShouldContain("'ConnectionStrings:clickhouse' is not configured");
    }

    [Fact]
    public void ClickHouseProvider_WithAnUnsafeTableName_FailsValidation()
    {
        using var host = BuildHost(new()
        {
            ["VectorStore:Provider"] = "ClickHouse",
            ["VectorStore:Table"] = "chunks; DROP TABLE users",
            ["ConnectionStrings:clickhouse"] = "Host=localhost;Port=8123;Username=default;Password=",
        });

        var exception = Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IVectorStore>());

        exception.Message.ShouldContain("VectorStore:Table 'chunks; DROP TABLE users' is not a valid ClickHouse identifier");
    }

    [Fact]
    public async Task ClickHouseProvider_IsReportedUnhealthy_WhenTheServerIsUnreachable()
    {
        using var host = BuildHost(new()
        {
            ["VectorStore:Provider"] = "ClickHouse",
            ["ConnectionStrings:clickhouse"] = "Host=127.0.0.1;Port=1;Username=default;Password=",
        });

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        report.Entries.TryGetValue("vectorstore", out var entry).ShouldBeTrue();
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Description.ShouldBe("ClickHouse vector store is not reachable.");
    }

    [Fact]
    public void Options_AreBoundFromTheVectorStoreSection()
    {
        using var host = BuildHost(new()
        {
            ["VectorStore:Provider"] = "InMemory",
            ["VectorStore:EnsureSchemaOnStartup"] = "false",
            ["VectorStore:Table"] = "custom_chunks",
        });

        var options = host.Services.GetRequiredService<IOptions<VectorStoreOptions>>().Value;

        options.EnsureSchemaOnStartup.ShouldBeFalse();
        options.Table.ShouldBe("custom_chunks");
    }

    [Fact]
    public void Provider_IsChosenWhenTheStoreIsResolved_SoLaterConfigurationWins()
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["VectorStore:Provider"] = "ClickHouse" });
        builder.AddVectorStore();

        // WebApplicationFactory adds its configuration only when the host is built.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["VectorStore:Provider"] = "InMemory" });
        using var host = builder.Build();

        host.Services.GetRequiredService<IVectorStore>().ShouldBeOfType<InMemoryVectorStore>();
    }

    [Fact]
    public void CallingTwice_RegistersEverythingOnce()
    {
        using var host = BuildHost(new() { ["VectorStore:Provider"] = "InMemory" }, builder => builder.AddVectorStore());

        host.Services.GetServices<IVectorStore>().Count().ShouldBe(1);
        host.Services.GetServices<IStartupInitializer>().Count().ShouldBe(1);
        host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Count(registration => registration.Name == "vectorstore").ShouldBe(1);
    }

    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        return builder;
    }

    private static IHost BuildHost(Dictionary<string, string?> settings, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddVectorStore();
        configure?.Invoke(builder);
        return builder.Build();
    }
}
