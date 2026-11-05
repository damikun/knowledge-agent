using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// A dedicated tracer provider limited to the agent sources keeps HTTP and database spans out of Langfuse.
public static class LangfuseTracing
{
    // Without this header Langfuse v4 does not ingest OTLP data in real time and new traces can take minutes to appear.
    public const string IngestionVersionHeader = "x-langfuse-ingestion-version=4";

    private static readonly string[] DefaultSources = ["KnowledgeAgent.Agents", "KnowledgeAgent.AI", "KnowledgeAgent.Evaluation"];

    public static TBuilder AddLangfuseTracing<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName;
        if (!TryGetSettings(builder.Configuration, out var settings))
        {
            return builder;
        }

        builder.Services.AddHostedService(serviceProvider => new LangfuseTracingService(
            serviceProvider.GetRequiredService<ILogger<LangfuseTracingService>>(),
            () => Build(settings, serviceName, resourceAttributes: null),
            settings.Endpoint));

        return builder;
    }

    /// <summary>For processes without hosted services. The caller flushes and disposes the provider.</summary>
    public static TracerProvider? CreateTracerProvider(
        IConfiguration configuration,
        string serviceName,
        IEnumerable<KeyValuePair<string, object>>? resourceAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return TryGetSettings(configuration, out var settings) ? Build(settings, serviceName, resourceAttributes) : null;
    }

    private static bool TryGetSettings(IConfiguration configuration, out ExporterSettings settings)
    {
        var section = configuration.GetSection("Langfuse");
        var baseUrl = section["BaseUrl"];
        var publicKey = section["PublicKey"];
        var secretKey = section["SecretKey"];

        if (!section.GetValue("Tracing:Enabled", true)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(secretKey))
        {
            settings = default;
            return false;
        }

        var sources = section.GetSection("Tracing:Sources").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : DefaultSources;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
        settings = new ExporterSettings(
            new Uri($"{baseUrl.TrimEnd('/')}/api/public/otel/v1/traces"),
            $"Authorization=Basic {credentials},{IngestionVersionHeader}",
            sources);
        return true;
    }

    private static TracerProvider Build(ExporterSettings settings, string serviceName, IEnumerable<KeyValuePair<string, object>>? resourceAttributes) =>
        Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName);
                if (resourceAttributes is not null)
                {
                    resource.AddAttributes(resourceAttributes);
                }
            })
            .AddSource(settings.Sources)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = settings.Endpoint;
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
                options.Headers = settings.Headers;
            })
            .Build();

    private readonly record struct ExporterSettings(Uri Endpoint, string Headers, string[] Sources);

    private sealed class LangfuseTracingService(
        ILogger<LangfuseTracingService> logger,
        Func<TracerProvider> createProvider,
        Uri endpoint) : IHostedService, IDisposable
    {
        private TracerProvider? _provider;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _provider = createProvider();
            logger.LogInformation("Exporting agent traces to Langfuse at {Endpoint}", endpoint);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _provider?.ForceFlush(5000);
            return Task.CompletedTask;
        }

        public void Dispose() => _provider?.Dispose();
    }
}
