using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalRunRecoveryInitializer(IServiceScopeFactory scopeFactory, EvalRunQueue queue) : IStartupInitializer
{
    public int Order => StartupOrder.Ingestion + 10;
    public string Name => "Eval run recovery";
    public bool IsCritical => false;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<IEvalRunRepository>();
        foreach (var run in await runs.ListByStatusAsync([EvalRunStatuses.Queued, EvalRunStatuses.Running], cancellationToken))
        {
            await queue.EnqueueAsync(run.Id, cancellationToken);
        }
    }
}
