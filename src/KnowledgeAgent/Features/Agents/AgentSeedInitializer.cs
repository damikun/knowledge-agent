using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Agents;

internal sealed partial class AgentSeedInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<AgentSeedInitializer> logger) : IStartupInitializer
{
    public int Order => StartupOrder.Agents;

    public string Name => "Starter agent";

    public bool IsCritical => true;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var agents = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        if ((await agents.ListAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        var agent = AgentResolver.Starter(options.Value);
        var now = timeProvider.GetUtcNow();
        agent.Id = Guid.CreateVersion7();
        agent.CreatedAt = now;
        agent.UpdatedAt = now;
        await agents.AddAsync(agent, cancellationToken);
        LogSeeded(logger, agent.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the starter agent '{Key}'")]
    private static partial void LogSeeded(ILogger logger, string key);
}
