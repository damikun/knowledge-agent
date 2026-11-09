using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Sources.Fakes;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Features.Sources;

public sealed class SourcesModuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Services_are_registered_with_the_expected_lifetimes()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Ingestion:MaxParallelism"] = "5",
            ["Ingestion:ChunkSizeTokens"] = "321",
            ["Ingestion:AllowPrivateNetworks"] = "true",
        }).Host;
        var services = host.Services;

        services.GetRequiredService<IIngestionQueue>().ShouldBeSameAs(services.GetRequiredService<IIngestionQueue>());
        services.GetRequiredService<IIngestionQueue>().ShouldBeOfType<IngestionQueue>();
        using (var scope = services.CreateScope())
        {
            // Persistence adds a scoped initializer, so the list resolves from a scope.
            scope.ServiceProvider.GetServices<IStartupInitializer>().OfType<IngestionRecoveryInitializer>().ShouldHaveSingleItem();
        }

        var ingestJob = services.GetServices<JobRegistration>().Single(job => job.Kind == IngestionJobs.IngestSource);
        ingestJob.HandlerType.ShouldBe(typeof(IngestSourceJob));
        ingestJob.Policy.MaxConcurrency.ShouldBe(5);
        services.GetServices<JobRegistration>().ShouldContain(job => job.Kind == IngestionJobs.RefreshWebSources);
        var refresh = services.GetServices<JobScheduleRegistration>().Single(schedule => schedule.Name == IngestionJobs.RefreshWebSourcesSchedule);
        refresh.Kind.ShouldBe(IngestionJobs.RefreshWebSources);
        refresh.Enabled.ShouldBeFalse();

        var options = services.GetRequiredService<IOptions<IngestionOptions>>().Value;
        options.MaxParallelism.ShouldBe(5);
        options.ChunkSizeTokens.ShouldBe(321);
        options.AllowPrivateNetworks.ShouldBeTrue();

        using var first = services.CreateScope();
        using var second = services.CreateScope();
        foreach (var type in new[] { typeof(IIngestionPipeline), typeof(ISourceService), typeof(IKnowledgeSearch) })
        {
            var a = first.ServiceProvider.GetRequiredService(type);
            a.ShouldBeSameAs(first.ServiceProvider.GetRequiredService(type), $"{type.Name} is scoped");
            a.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService(type), $"{type.Name} is scoped");
        }
    }

    [Fact]
    public void Fetch_client_uses_a_hardened_handler()
    {
        using var host = BuildHost().Host;

        var handler = host.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(UrlFetcher.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        var sockets = handler.ShouldBeOfType<SocketsHttpHandler>();
        sockets.AllowAutoRedirect.ShouldBeFalse();
        sockets.UseProxy.ShouldBeFalse();
        sockets.UseCookies.ShouldBeFalse();
        sockets.ConnectCallback.ShouldNotBeNull();
    }

    [Fact]
    public void Nonsensical_options_fail_validation()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Ingestion:MaxParallelism"] = "0",
            ["Ingestion:EmbeddingBatchSize"] = "0",
        }).Host;

        var ex = Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<IngestionOptions>>().Value);

        ex.Failures.ShouldContain(f => f.Contains("MaxParallelism"));
        ex.Failures.ShouldContain(f => f.Contains("EmbeddingBatchSize"));
    }

    [Fact]
    public async Task Added_sources_are_ingested_by_background_jobs_and_become_searchable()
    {
        await using var server = await TestHttpServer.StartAsync();
        server.AddHtml("article", TestDocuments.ArticleHtml);
        var (host, repository, vectorStore) = BuildHost(new Dictionary<string, string?>
        {
            ["Ingestion:AllowPrivateNetworks"] = "true",
            ["Ingestion:MaxParallelism"] = "2",
        });
        using (host)
        {
            await host.StartAsync(Ct);
            Source text, url;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var sources = scope.ServiceProvider.GetRequiredService<ISourceService>();
                text = await sources.AddTextAsync("Release plan", "The release train leaves every second Thursday after the QA sign-off.", cancellationToken: Ct);
                url = await sources.AddUrlAsync(server.Url("article").AbsoluteUri, cancellationToken: Ct);
            }

            await WaitUntilAsync(() => repository.Find(text.Id)?.Status == SourceStatus.Ready && repository.Find(url.Id)?.Status == SourceStatus.Ready);

            repository.Find(url.Id)!.Title.ShouldBe("Understanding Vector Databases");
            vectorStore.ForSource(text.Id).ShouldNotBeEmpty();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var search = scope.ServiceProvider.GetRequiredService<IKnowledgeSearch>();
                var hits = (await search.SearchAsync(new KnowledgeSearchRequest("release train Thursday sign-off", TopK: 3), Ct)).Hits;
                hits[0].SourceId.ShouldBe(text.Id);
                hits[0].SourceTitle.ShouldBe("Release plan");
            }

            await host.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Startup_recovery_resumes_interrupted_ingestion()
    {
        var (host, repository, _) = BuildHost();
        using (host)
        {
            var interrupted = new Source { Type = SourceType.Text, Title = "Interrupted", Status = SourceStatus.Processing, Attempts = 1 };
            await repository.AddAsync(interrupted, new SourceContent { SourceId = interrupted.Id, ExtractedText = "Recovered after a crash." }, Ct);

            await host.StartAsync(Ct);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                foreach (var initializer in scope.ServiceProvider.GetServices<IStartupInitializer>().OrderBy(i => i.Order))
                {
                    await initializer.InitializeAsync(Ct);
                }
            }

            await WaitUntilAsync(() => repository.Find(interrupted.Id)?.Status == SourceStatus.Ready);
            repository.Find(interrupted.Id)!.Attempts.ShouldBe(2);
            await host.StopAsync(Ct);
        }
    }

    private static (IHost Host, InMemorySourceRepository Repository, InMemoryVectorStore VectorStore) BuildHost(
        Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = false,
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceModule.InMemoryProvider,
            ["Persistence:InMemoryDatabaseName"] = $"sources-module-{Guid.NewGuid():N}",
        });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        builder.AddPersistence();
        builder.AddJobs();
        builder.AddSources();
        builder.AddSearch();
        builder.Services.RemoveAll<IRerankService>();

        // The repository is scoped like the EF Core implementation.
        var repository = new InMemorySourceRepository();
        var vectorStore = new InMemoryVectorStore();
        builder.Services.Replace(ServiceDescriptor.Scoped<ISourceRepository>(_ => repository));
        builder.Services.AddSingleton<IVectorStore>(vectorStore);
        builder.Services.AddSingleton<IEmbeddingService>(new FakeEmbeddingService());

        return (builder.Build(), repository, vectorStore);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
