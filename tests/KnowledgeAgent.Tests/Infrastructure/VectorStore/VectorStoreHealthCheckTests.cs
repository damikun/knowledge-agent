using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Tests.Infrastructure.VectorStore.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeAgent.Tests.Infrastructure.VectorStore;

public sealed class VectorStoreHealthCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IsHealthy_WhenThePingSucceeds()
    {
        var check = new VectorStoreHealthCheck(new FakeVectorStore { PingResult = true });

        var result = await check.CheckHealthAsync(Context(check), Ct);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe("Fake vector store is reachable.");
        result.Data["provider"].ShouldBe("Fake");
    }

    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task ReportsTheRegisteredFailureStatus_WhenThePingFails(HealthStatus failureStatus)
    {
        var check = new VectorStoreHealthCheck(new FakeVectorStore { PingResult = false });

        var result = await check.CheckHealthAsync(Context(check, failureStatus), Ct);

        result.Status.ShouldBe(failureStatus);
        result.Description.ShouldBe("Fake vector store is not reachable.");
    }

    private static HealthCheckContext Context(IHealthCheck check, HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new() { Registration = new HealthCheckRegistration(VectorStoreHealthCheck.Name, check, failureStatus, tags: null) };
}
