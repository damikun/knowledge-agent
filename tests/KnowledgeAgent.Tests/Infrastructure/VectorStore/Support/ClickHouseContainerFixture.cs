using KnowledgeAgent.TestKit;
using Testcontainers.ClickHouse;

[assembly: AssemblyFixture(typeof(KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.ClickHouseContainerFixture))]

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;

// Started lazily by the first test that needs it, so in-memory-only runs never touch Docker.
public sealed class ClickHouseContainerFixture : IAsyncLifetime
{
    private readonly Lock _gate = new();
    private Task<ClickHouseContainer>? _container;

    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken)
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        Task<ClickHouseContainer> start;
        lock (_gate)
        {
            // Not tied to one test's token: the container is shared by all tests.
            start = _container ??= StartContainerAsync();
        }

        var container = await start.WaitAsync(cancellationToken);
        return container.GetConnectionString();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        Task<ClickHouseContainer>? start;
        lock (_gate)
        {
            start = _container;
        }

        if (start is null)
        {
            return;
        }

        try
        {
            await (await start).DisposeAsync();
        }
        catch (Exception) when (start.IsFaulted || start.IsCanceled)
        {
            // The failed start already disposed the container and reported the error to the tests.
        }
    }

    private static async Task<ClickHouseContainer> StartContainerAsync()
    {
        var container = new ClickHouseBuilder(ContainerImages.ClickHouse).Build();
        try
        {
            await container.StartAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }
}
