using KnowledgeAgent.Features.Models;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Infrastructure.AI.Support;

internal sealed class AiTestHost : IDisposable
{
    private readonly IHost _host;
    private readonly IConfigurationRoot _configuration;

    private AiTestHost(IHost host, IConfigurationRoot configuration, InMemoryOverrideState overrides)
    {
        _host = host;
        _configuration = configuration;
        Overrides = overrides;
    }

    public InMemoryOverrideState Overrides { get; }

    public IServiceProvider Services => _host.Services;

    public IModelProfileResolver Resolver => Get<IModelProfileResolver>();

    public IChatClientFactory ChatClients => Get<IChatClientFactory>();

    public IEmbeddingService Embeddings => Get<IEmbeddingService>();

    public AiOptions Options => Get<IOptionsMonitor<AiOptions>>().CurrentValue;

    public static AiTestHost Create(
        Dictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null,
        bool registerOverrideStore = true)
    {
        // An empty builder reads no environment variables, so the machine's API keys never leak into tests.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Configuration.AddInMemoryCollection(configuration ?? TestConfig.Base());
        builder.AddAi();
        builder.AddModels();
        builder.Services.RemoveAll<IModelProfileOverrideStore>();
        builder.Services.RemoveAll<IProviderSettingsStore>();
        builder.Services.TryAddSingleton<IRerankService, RerankService>();

        var overrides = new InMemoryOverrideState();
        builder.Services.AddSingleton(overrides);
        if (registerOverrideStore)
        {
            builder.Services.AddScoped<IModelProfileOverrideStore, InMemoryModelProfileOverrideStore>();
        }

        configureServices?.Invoke(builder.Services);
        return new AiTestHost(builder.Build(), builder.Configuration, overrides);
    }

    public T Get<T>() where T : notnull => _host.Services.GetRequiredService<T>();

    public void Reconfigure(params (string Key, string? Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            _configuration[key] = value;
        }

        _configuration.Reload();
    }

    public void Dispose() => _host.Dispose();
}
