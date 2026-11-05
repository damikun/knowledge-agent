using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Configuration;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;

namespace KnowledgeAgent.Features.Evaluations.Running;

public sealed class IsolatedTargetOptions
{
    public required IConfiguration Configuration { get; init; }
    public IServiceProvider? Parent { get; init; }
    public IReadOnlyDictionary<string, string?>? Overrides { get; init; }
    public Action<IServiceCollection>? ConfigureServices { get; init; }
    public ILoggerFactory? LoggerFactory { get; init; }
}

public sealed class IsolatedEvalTarget : IEvalTarget
{
    private readonly IHost _host;
    private readonly Dictionary<string, Source> _sourcesByKey;

    private IsolatedEvalTarget(IHost host, Dictionary<string, Source> sourcesByKey)
    {
        _host = host;
        _sourcesByKey = sourcesByKey;
    }

    public string Kind => EvalTargets.Isolated;

    public IServiceProvider Services => _host.Services;

    public IReadOnlyDictionary<string, Source> Sources => _sourcesByKey;

    public static async Task<IsolatedEvalTarget> CreateAsync(
        IsolatedTargetOptions options,
        IReadOnlyList<EvalCorpusDocument> corpus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(corpus);

        var host = Build(options);
        try
        {
            await host.Services.RunStartupInitializersAsync(NullLogger.Instance, cancellationToken);
            var sources = await IngestAsync(host.Services, corpus, cancellationToken);
            return new IsolatedEvalTarget(host, sources);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public Task<ResolvedSources> ResolveSourcesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, IReadOnlySet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            var matches = _sourcesByKey
                .Where(entry => string.Equals(entry.Key, reference, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Value.Title, reference, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Value.Id.ToString("D"), reference, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Value.Id)
                .ToHashSet();
            resolved[reference] = matches;
        }

        return Task.FromResult(new ResolvedSources(resolved));
    }

    public Task CleanupConversationAsync(Guid conversationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IHost Build(IsolatedTargetOptions options)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
            ApplicationName = "KnowledgeAgent.Evaluation",
        });

        builder.Configuration.AddConfiguration(options.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceModule.InMemoryProvider,
            ["Persistence:InMemoryDatabaseName"] = $"eval-{Guid.NewGuid():N}",

            // The isolated knowledge base never hands its ingestion to the app's job engine.
            ["Jobs:Provider"] = JobEngines.InProcess,
            ["VectorStore:Provider"] = "InMemory",
            ["VectorStore:EnsureSchemaOnStartup"] = "false",
            ["Prompts:SeedOnStartup"] = "false",

            // Titles cost an extra model call per question and are discarded with the database anyway.
            ["Agents:GenerateTitles"] = "false",
        });
        if (options.Overrides is { Count: > 0 } overrides)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        if (options.LoggerFactory is not null)
        {
            builder.Services.AddSingleton(options.LoggerFactory);
        }

        builder.AddPersistence();

        // The host is never started, so no runner picks up the ingestion jobs and IngestAsync runs the pipeline itself.
        builder.AddJobs();
        builder.AddVectorStore();
        builder.AddPrompts();
        builder.AddAi();
        builder.AddSources();
        builder.AddSearch();
        builder.AddChat();

        if (options.Parent is { } parent)
        {
            // The running app's AI and prompt services, so keys and overrides saved in the portal apply.
            // Not disposed by this host.
            builder.Services.AddSingleton(parent.GetRequiredService<IChatClientFactory>());
            builder.Services.AddSingleton(parent.GetRequiredService<IEmbeddingService>());
            if (parent.GetService<IRerankService>() is { } reranker)
            {
                builder.Services.AddSingleton(reranker);
            }

            builder.Services.AddSingleton(parent.GetRequiredService<IModelProfileResolver>());
            builder.Services.AddSingleton(parent.GetRequiredService<IAiOptionsSource>());
            builder.Services.AddSingleton(parent.GetRequiredService<IPromptService>());
        }

        options.ConfigureServices?.Invoke(builder.Services);
        return builder.Build();
    }

    private static async Task<Dictionary<string, Source>> IngestAsync(
        IServiceProvider services,
        IReadOnlyList<EvalCorpusDocument> corpus,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);

        using var suppress = SuppressInstrumentationScope.Begin();
        foreach (var document in corpus)
        {
            await using var scope = services.CreateAsyncScope();
            var sourceService = scope.ServiceProvider.GetRequiredService<ISourceService>();
            var added = await sourceService.AddFileAsync(
                document.FileName, ContentType(document.FileName), document.Content, document.Title, cancellationToken: cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IIngestionPipeline>().ProcessAsync(added.Id, cancellationToken);

            var source = await sourceService.GetAsync(added.Id, cancellationToken);
            if (source is not { Status: SourceStatus.Ready })
            {
                throw new EvalRunException(
                    $"Corpus document '{document.FileName}' could not be ingested: {source?.Error ?? "it is not ready after ingestion"}.");
            }

            sources[document.Key] = source;
        }

        return sources;
    }

    private static string? ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".md" or ".markdown" => "text/markdown",
        ".txt" => "text/plain",
        ".pdf" => "application/pdf",
        ".html" or ".htm" => "text/html",
        _ => null,
    };
}

public static class EvalTargets
{
    public const string Isolated = "isolated";
    public const string Live = "live";
}
