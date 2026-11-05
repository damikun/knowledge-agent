using System.Globalization;
using KnowledgeAgent.TestKit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Support;

public enum TestMode
{
    InMemory,
    Containers,
}

public sealed class KnowledgeAgentAppOptions
{
    public bool LangfuseUnavailable { get; set; }

    public Dictionary<string, string?> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Action<IServiceCollection>? ConfigureServices { get; set; }
}

public sealed class KnowledgeAgentApp : IAsyncDisposable
{
    private readonly List<Exception> _shutdownErrors = [];
    private int _stopped;
    private int _disposed;

    private KnowledgeAgentApp(
        TestMode mode,
        KnowledgeAgentFactory factory,
        HttpClient client,
        FakeLangfuseServer langfuse,
        FakeAiProvider aiProvider,
        FakeChatModels chatModels,
        TestLogSink logs)
    {
        Mode = mode;
        Factory = factory;
        Client = client;
        Langfuse = langfuse;
        AiProvider = aiProvider;
        ChatModels = chatModels;
        Logs = logs;
    }

    public TestMode Mode { get; }
    public KnowledgeAgentFactory Factory { get; }
    public HttpClient Client { get; }
    public FakeLangfuseServer Langfuse { get; }
    public FakeAiProvider AiProvider { get; }
    public FakeChatModels ChatModels { get; }
    public TestLogSink Logs { get; }
    public IServiceProvider Services => Factory.Services;

    // Collected rather than thrown, so one shutdown bug does not fail every test class.
    public IReadOnlyList<Exception> ShutdownErrors => _shutdownErrors;

    public static async Task<KnowledgeAgentApp> StartAsync(
        TestMode mode,
        IReadOnlyDictionary<string, string?> storageSettings,
        KnowledgeAgentAppOptions options)
    {
        var langfuse = await FakeLangfuseServer.StartAsync();
        langfuse.Unavailable = options.LangfuseUnavailable;

        var chatModels = new FakeChatModels();
        var aiProvider = new FakeAiProvider(profile => new SwitchableChatClient(chatModels, profile));
        var logs = new TestLogSink();
        var settings = BuildSettings(storageSettings, langfuse, options);
        var factory = new KnowledgeAgentFactory(settings, aiProvider, logs, options.ConfigureServices);

        try
        {
            // Program and its startup initializers run during CreateClient, so keep them off the test thread.
            var client = await Task.Run(() => factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true,
                HandleCookies = false,
            }));
            client.Timeout = TimeSpan.FromMinutes(2);
            return new KnowledgeAgentApp(mode, factory, client, langfuse, aiProvider, chatModels, logs);
        }
        catch (Exception exception)
        {
            await DisposeQuietlyAsync(factory);
            await langfuse.DisposeAsync();
            throw new InvalidOperationException(
                $"The API failed to start in {mode} mode.{Environment.NewLine}Recent server logs:{Environment.NewLine}{logs.Dump()}",
                exception);
        }
    }

    internal static Dictionary<string, string?> BuildSettings(
        IReadOnlyDictionary<string, string?> storageSettings,
        FakeLangfuseServer langfuse,
        KnowledgeAgentAppOptions options)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Prompts:Store"] = "Langfuse",
            ["Prompts:Label"] = "production",
            ["Prompts:CacheTtlSeconds"] = "0",
            ["Prompts:SeedOnStartup"] = "true",
            ["Prompts:FallbackToLocal"] = "true",
            ["Langfuse:BaseUrl"] = langfuse.BaseAddress.ToString().TrimEnd('/'),
            ["Langfuse:PublicUrl"] = string.Empty,
            ["Langfuse:PublicKey"] = langfuse.PublicKey,
            ["Langfuse:SecretKey"] = langfuse.SecretKey,
            ["Langfuse:ProjectId"] = FakeLangfuseServer.ProjectId,
            ["Langfuse:Tracing:Enabled"] = "true",
            ["Ingestion:AllowPrivateNetworks"] = "true",
            ["Ingestion:MaxParallelism"] = "2",
            ["Ingestion:FetchTimeoutSeconds"] = "15",
            ["AI:Providers:fake:Type"] = FakeAiProvider.TypeName,
            ["AI:Providers:fake:DefaultChatModel"] = FakeModels.ChatModel,
            ["AI:Providers:fake:DefaultEmbeddingModel"] = FakeModels.EmbeddingModel,
            ["AI:Providers:fake:DefaultEmbeddingDimensions"] = FakeModels.EmbeddingDimensions.ToString(CultureInfo.InvariantCulture),
            ["AI:Providers:anthropic:ApiKey"] = string.Empty,
            ["AI:Providers:openai:ApiKey"] = string.Empty,
            ["AI:Providers:ollama:Endpoint"] = string.Empty,

            // Blank the well-known fallback variables so a developer's real key is never used.
            ["ANTHROPIC_API_KEY"] = string.Empty,
            ["OPENAI_API_KEY"] = string.Empty,
            ["VOYAGE_API_KEY"] = string.Empty,
            ["AI:Providers:voyage:ApiKey"] = string.Empty,
            ["OLLAMA_ENDPOINT"] = string.Empty,
            ["AI:Profiles:orchestrator:Provider"] = FakeModels.ProviderKey,
            ["AI:Profiles:researcher:Provider"] = FakeModels.ProviderKey,
            ["AI:Profiles:researcher:Model"] = FakeModels.ResearcherModel,
            ["AI:Profiles:utility:Provider"] = FakeModels.ProviderKey,
            ["AI:Profiles:utility:Model"] = FakeModels.UtilityModel,
            ["AI:Profiles:embedding:Provider"] = FakeModels.ProviderKey,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,

            // Every request is the local admin; AuthScenarios act as other users through TestUsers.
            ["Auth:Mode"] = "None",
        };

        foreach (var (key, value) in storageSettings)
        {
            settings[key] = value;
        }

        foreach (var (key, value) in options.Settings)
        {
            settings[key] = value;
        }

        return settings;
    }

    // Keeps the fake Langfuse server running so tests can assert on what the app exported.
    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        Client.Dispose();
        try
        {
            await Factory.DisposeAsync();
        }
        catch (Exception exception)
        {
            _shutdownErrors.Add(exception);
            Logs.Add($"{DateTime.UtcNow:HH:mm:ss.fff} [fail] Shutdown: {exception}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync();
        }
        finally
        {
            await Langfuse.DisposeAsync();
        }
    }

    private static async Task DisposeQuietlyAsync(IAsyncDisposable disposable)
    {
        try
        {
            await disposable.DisposeAsync();
        }
        catch (Exception)
        {
            // Startup already failed, and the original exception is the one that matters.
        }
    }
}
