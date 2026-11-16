using KnowledgeAgent.TestKit;
using Testcontainers.ClickHouse;

[assembly: AssemblyFixture(typeof(KnowledgeAgent.Tests.Infrastructure.VectorStore.Support.ClickHouseContainerFixture))]

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;

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
