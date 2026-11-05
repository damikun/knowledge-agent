using KnowledgeAgent.Features.Sources.Chunking;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources;

public static class SourcesModule
{
    public static IHostApplicationBuilder AddSources(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSources(builder.Configuration.GetSection(IngestionOptions.SectionName));
        builder.Services.AddSourceFolders();
        return builder;
    }

    internal static IServiceCollection AddSources(this IServiceCollection services, IConfiguration ingestionSection)
    {
        services.AddOptions<IngestionOptions>().Bind(ingestionSection).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>());
        services.AddOptions<FormOptions>().Configure<IConfiguration>((options, configuration) =>
            options.MultipartBodyLengthLimit = SourceEndpoints.UploadRequestLimit(configuration));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<ISourceRepository, SourceRepository>();
        services.TryAddScoped<ISourceService, SourceService>();
        services.TryAddScoped<IngestionPipeline>();
        services.TryAddScoped<IIngestionPipeline>(sp => sp.GetRequiredService<IngestionPipeline>());
        services.TryAddScoped<WebsiteCrawler>();
        services.TryAddSingleton<HostRateLimiter>();

        services.TryAddSingleton<IIngestionQueue, IngestionQueue>();
        // Read from configuration here because the job's concurrency is fixed when the job is registered.
        var maxParallelism = ingestionSection.Get<IngestionOptions>()?.MaxParallelism ?? new IngestionOptions().MaxParallelism;
        services.AddJob<IngestSourceJob>(IngestionJobs.IngestSource, new JobPolicy
        {
            MaxConcurrency = Math.Clamp(maxParallelism, 1, IngestionJobs.MaxConcurrency),
            MaxAttempts = 3,
            // Two quick Re-ingest clicks, or an edit right after an upload, cost one run.
            Debounce = IngestionJobs.Debounce,
        });
        services.AddJob<CrawlWebsiteJob>(IngestionJobs.CrawlWebsite, new JobPolicy
        {
            MaxConcurrency = IngestionJobs.MaxConcurrentCrawls,
            MaxAttempts = 3,
            Debounce = IngestionJobs.Debounce,
        });
        services.AddJob<RefreshWebSourcesJob>(IngestionJobs.RefreshWebSources, JobPolicy.Default with { Overlap = JobOverlap.Skip });
        services.AddJobSchedule(new JobScheduleRegistration(
            IngestionJobs.RefreshWebSourcesSchedule, IngestionJobs.RefreshWebSources, "all", "0 4 * * *", Enabled: false));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupInitializer, IngestionRecoveryInitializer>());
        services.TryAddSingleton<WaitingSourcesQueuer>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelSettingsListener, WaitingSourcesQueuer>(sp => sp.GetRequiredService<WaitingSourcesQueuer>()));

        services.AddHttpClient(UrlFetcher.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp => UrlFetcher.CreateHandler(
                sp.GetRequiredService<IOptionsMonitor<IngestionOptions>>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SafeConnector).FullName!)));
        services.TryAddSingleton<UrlFetcher>();
        services.TryAddSingleton<HtmlTextExtractor>();
        services.TryAddSingleton<PdfTextExtractor>();
        services.TryAddSingleton<ContentExtractor>();
        services.TryAddSingleton<TextChunker>();
        services.TryAddScoped<IChunkContextualizer, ChunkContextualizer>();

        return services;
    }

    // Folders only organize the library view; ingestion and search work without them.
    internal static IServiceCollection AddSourceFolders(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SourceFolderGate>();
        services.TryAddScoped<ISourceFolderRepository, SourceFolderRepository>();
        services.TryAddScoped<ISourceFolderService, SourceFolderService>();
        return services;
    }
}
