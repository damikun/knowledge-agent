using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed class VectorStoreHealthCheck(IVectorStore store) : IHealthCheck
{
    public const string Name = "vectorstore";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["provider"] = store.ProviderName };
        return await store.PingAsync(cancellationToken)
            ? HealthCheckResult.Healthy($"{store.ProviderName} vector store is reachable.", data)
            : new HealthCheckResult(context.Registration.FailureStatus, $"{store.ProviderName} vector store is not reachable.", data: data);
    }
}
