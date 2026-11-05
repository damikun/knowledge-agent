using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Prompts.Langfuse;

internal sealed class LangfuseHealthCheck(LangfuseClient client, IOptions<LangfuseOptions> options) : IHealthCheck
{
    public const string Name = "langfuse";

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.IsConfigured)
        {
            return HealthCheckResult.Healthy("not configured");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var health = await client.GetHealthAsync(timeout.Token).ConfigureAwait(false);
            return HealthCheckResult.Healthy(
                "reachable",
                new Dictionary<string, object> { ["status"] = health.Status, ["version"] = health.Version ?? "unknown" });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Degraded rather than unhealthy: the app keeps working with cached or local prompts.
            var reason = timeout.IsCancellationRequested ? $"no answer within {Timeout.TotalSeconds:0} s" : ex.Message;
            return HealthCheckResult.Degraded($"configured but unreachable: {reason}", ex);
        }
    }
}
