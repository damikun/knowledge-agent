using KnowledgeAgent.TestKit;

namespace KnowledgeAgent.Api.Tests.Support;

// Tests in a class run sequentially, so they can share one lazily started app.
public abstract class AppFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KnowledgeAgentApp? _app;
    private TestHttpServer? _webServer;

    public abstract TestMode Mode { get; }

    public async Task<KnowledgeAgentApp> GetAppAsync()
    {
        EnsureAvailable();
        await _gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return _app ??= await StartAppAsync(new KnowledgeAgentAppOptions());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KnowledgeAgentApp> CreateAppAsync(Action<KnowledgeAgentAppOptions>? configure = null)
    {
        EnsureAvailable();
        var options = new KnowledgeAgentAppOptions();
        configure?.Invoke(options);
        return await StartAppAsync(options);
    }

    // Kept for the fixture's lifetime so re-ingestion can fetch the pages again.
    public async Task<TestHttpServer> GetWebServerAsync()
    {
        await _gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return _webServer ??= await TestHttpServer.StartAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    protected virtual void EnsureAvailable()
    {
    }

    protected abstract Task<IReadOnlyDictionary<string, string?>> CreateStorageSettingsAsync();

    private async Task<KnowledgeAgentApp> StartAppAsync(KnowledgeAgentAppOptions options) =>
        await KnowledgeAgentApp.StartAsync(Mode, await CreateStorageSettingsAsync(), options);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
        finally
        {
            if (_webServer is not null)
            {
                await _webServer.DisposeAsync();
            }

            _gate.Dispose();
        }
    }
}

public sealed class InMemoryAppFixture : AppFixture
{
    public override TestMode Mode => TestMode.InMemory;

    protected override Task<IReadOnlyDictionary<string, string?>> CreateStorageSettingsAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["Persistence:InMemoryDatabaseName"] = $"knowledge-e2e-{Guid.NewGuid():N}",
            ["VectorStore:Provider"] = "InMemory",
        });
}

public sealed class ContainersAppFixture : AppFixture
{
    public override TestMode Mode => TestMode.Containers;

    protected override void EnsureAvailable() => Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

    protected override Task<IReadOnlyDictionary<string, string?>> CreateStorageSettingsAsync() => SharedContainers.CreateIsolatedStorageAsync();
}
