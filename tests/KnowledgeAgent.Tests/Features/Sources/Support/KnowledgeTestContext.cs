using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Tests.Features.Sources.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Sources.Support;

internal sealed class KnowledgeTestContext : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    public KnowledgeTestContext(
        Action<IngestionOptions>? configure = null,
        Action<SearchOptions>? configureSearch = null,
        IRerankService? reranker = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSources(configuration.GetSection(IngestionOptions.SectionName));
        services.AddSearch();
        services.RemoveAll<IRerankService>();
        services.Configure<IngestionOptions>(options =>
        {
            options.AllowPrivateNetworks = true;
            options.FetchTimeoutSeconds = 10;
            configure?.Invoke(options);
        });
        services.Replace(ServiceDescriptor.Singleton<ISourceRepository>(Repository));
        services.Replace(ServiceDescriptor.Singleton<IIngestionQueue>(Queue));
        services.AddSingleton<IVectorStore>(VectorStore);
        services.AddSingleton<IEmbeddingService>(Embeddings);
        if (configureSearch is not null)
        {
            services.Configure(configureSearch);
        }

        if (reranker is not null)
        {
            services.AddSingleton(reranker);
        }

        configureServices?.Invoke(services);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public InMemorySourceRepository Repository { get; } = new();

    public InMemoryVectorStore VectorStore { get; } = new();

    public FakeEmbeddingService Embeddings { get; } = new();

    public FakeTimeProvider Time { get; } = new(Start);

    public ServiceProvider Services { get; }

    public RecordingIngestionQueue Queue { get; } = new();

    public Task ProcessAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        ProcessAsync(sourceId, jobRunId: null, cancellationToken);

    public async Task ProcessAsync(Guid sourceId, string? jobRunId, CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIngestionPipeline>().ProcessAsync(sourceId, jobRunId, cancellationToken);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<T> WithSourceServiceAsync<T>(Func<ISourceService, Task<T>> action) =>
        WithScopeAsync(sp => action(sp.GetRequiredService<ISourceService>()));

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(KnowledgeSearchRequest request) =>
        (await SearchResultAsync(request)).Hits;

    public Task<KnowledgeSearchResult> SearchResultAsync(KnowledgeSearchRequest request) =>
        WithScopeAsync(sp => sp.GetRequiredService<IKnowledgeSearch>().SearchAsync(request, TestContext.Current.CancellationToken));

    public async Task<Source> SeedAsync(Source source, SourceContent? content = null)
    {
        source.CreatedAt = source.UpdatedAt = Time.GetUtcNow();
        await Repository.AddAsync(source, content, TestContext.Current.CancellationToken);
        return source;
    }

    public Task<Source> SeedTextAsync(string title, string text, SourceType type = SourceType.Text)
    {
        var source = new Source { Type = type, Title = title, ContentType = "text/plain" };
        return SeedAsync(source, new SourceContent { SourceId = source.Id, ExtractedText = text });
    }

    // Sources queued to run now; a delayed retry fails the check, so tests assert those through Queue.Drain().
    public List<Guid> DrainQueue()
    {
        var queued = Queue.Drain();
        queued.ShouldAllBe(item => item.Delay == TimeSpan.Zero);
        return [.. queued.Select(item => item.SourceId)];
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
