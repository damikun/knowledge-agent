using System.Diagnostics;
using ClickHouse.Driver;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.VectorStore.ClickHouse;
using KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore;

public sealed class VectorStoreSchemaInitializerTests
{
    private static readonly StartupRetryPolicy FastPolicy = new(
        Timeout: TimeSpan.FromSeconds(5),
        InitialDelay: TimeSpan.FromMilliseconds(10),
        MaxDelay: TimeSpan.FromMilliseconds(50),
        AttemptTimeout: TimeSpan.FromSeconds(2));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void RunsAsTheCriticalVectorStoreStep()
    {
        IStartupInitializer initializer = CreateInitializer(new FakeVectorStore());

        initializer.Order.ShouldBe(StartupOrder.VectorStore);
        initializer.Name.ShouldBe("VectorStore");
        initializer.IsCritical.ShouldBeTrue();
    }

    [Fact]
    public void DefaultRetryBudget_IsAboutOneMinute()
    {
        StartupRetryPolicy.Default.Timeout.ShouldBe(TimeSpan.FromSeconds(60));
        StartupRetryPolicy.Default.InitialDelay.ShouldBeLessThan(StartupRetryPolicy.Default.MaxDelay);
    }

    [Fact]
    public async Task EnsuresTheSchema()
    {
        var store = new FakeVectorStore();

        await CreateInitializer(store).InitializeAsync(Ct);

        store.EnsureSchemaCalls.ShouldBe(1);
    }

    [Fact]
    public async Task DoesNothing_WhenEnsureSchemaOnStartupIsDisabled()
    {
        var store = new FakeVectorStore();

        await CreateInitializer(store, ensureSchemaOnStartup: false).InitializeAsync(Ct);

        store.EnsureSchemaCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RetriesTransientFailures_UntilTheStoreIsReady()
    {
        var store = new FakeVectorStore
        {
            OnEnsureSchema = (attempt, _) => attempt switch
            {
                1 => throw new HttpRequestException("Connection refused (127.0.0.1:8123)"),
                2 => Task.FromException(new ClickHouseServerException("Code: 210. DB::Exception: Connection reset by peer", "CREATE DATABASE", 210)),
                3 => Task.FromException(new IOException("The response ended prematurely.")),
                _ => Task.CompletedTask,
            },
        };

        await CreateInitializer(store).InitializeAsync(Ct);

        store.EnsureSchemaCalls.ShouldBe(4);
    }

    [Fact]
    public async Task GivesUpAfterTheRetryBudget_WithTheLastErrorAsInnerException()
    {
        var store = new FakeVectorStore { OnEnsureSchema = (_, _) => throw new HttpRequestException("Connection refused") };
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateInitializer(store, policy: FastPolicy with { Timeout = TimeSpan.FromMilliseconds(300) }).InitializeAsync(Ct));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        exception.Message.ShouldContain("The Fake vector store could not be initialized within 0.3 s");
        exception.InnerException.ShouldBeOfType<HttpRequestException>();
        store.EnsureSchemaCalls.ShouldBeGreaterThan(2);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("configuration")]
    [InlineData("authentication")]
    [InlineData("access-denied")]
    public async Task FailsImmediately_OnErrorsThatRetryingCannotFix(string kind)
    {
        Exception error = kind switch
        {
            "schema" => new VectorStoreSchemaException("Table exists but does not match the vector store schema."),
            "configuration" => new ArgumentException("VectorStore:Table 'a b' is not a valid ClickHouse identifier."),
            "authentication" => new ClickHouseServerException("Code: 516. DB::Exception: Authentication failed", "SELECT 1", 516),
            "access-denied" => new ClickHouseServerException("Code: 497. DB::Exception: Not enough privileges", "CREATE DATABASE", 497),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var store = new FakeVectorStore { OnEnsureSchema = (_, _) => Task.FromException(error) };

        var thrown = await Should.ThrowAsync<Exception>(() => CreateInitializer(store).InitializeAsync(Ct));

        thrown.ShouldBeSameAs(error);
        store.EnsureSchemaCalls.ShouldBe(1);
    }

    [Fact]
    public async Task AbandonsAHangingAttempt_AndRetries()
    {
        var store = new FakeVectorStore
        {
            OnEnsureSchema = async (attempt, cancellationToken) =>
            {
                if (attempt == 1)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
            },
        };

        await CreateInitializer(store, policy: FastPolicy with { AttemptTimeout = TimeSpan.FromMilliseconds(100) }).InitializeAsync(Ct);

        store.EnsureSchemaCalls.ShouldBe(2);
    }

    [Fact]
    public async Task StopsRetrying_WhenStartupIsCancelled()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var store = new FakeVectorStore { OnEnsureSchema = (_, _) => throw new HttpRequestException("Connection refused") };
        var policy = FastPolicy with { Timeout = TimeSpan.FromMinutes(1), InitialDelay = TimeSpan.FromSeconds(30), MaxDelay = TimeSpan.FromSeconds(30) };
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateInitializer(store, policy: policy).InitializeAsync(cancellation.Token));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        store.EnsureSchemaCalls.ShouldBe(1);
    }

    [Fact]
    public async Task UnreachableClickHouse_FailsAfterTheRetryBudget()
    {
        // Nothing listens on port 1, so connections are refused immediately and no Docker is needed.
        using var store = new ClickHouseVectorStore(
            "Host=127.0.0.1;Port=1;Username=default;Password=",
            "knowledge",
            "chunks",
            NullLogger<ClickHouseVectorStore>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CreateInitializer(store, policy: FastPolicy with { Timeout = TimeSpan.FromMilliseconds(500) }).InitializeAsync(Ct));

        exception.Message.ShouldContain("The ClickHouse vector store could not be initialized");
        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    private static VectorStoreSchemaInitializer CreateInitializer(
        IVectorStore store,
        bool ensureSchemaOnStartup = true,
        StartupRetryPolicy? policy = null) =>
        new(
            store,
            Options.Create(new VectorStoreOptions { EnsureSchemaOnStartup = ensureSchemaOnStartup }),
            NullLogger<VectorStoreSchemaInitializer>.Instance,
            policy ?? FastPolicy);
}
