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

    public string Name => "Built-in agent";

    public bool IsCritical => true;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var agents = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        if (await agents.GetByKeyAsync(AgentConfig.InternalKey, cancellationToken) is not null)
        {
            return;
        }

        var agent = AgentResolver.Builtin(options.Value);
        var now = timeProvider.GetUtcNow();
        agent.Id = Guid.CreateVersion7();
        agent.Description = "The agent of the portal's chat, for admins and support staff.";
        agent.CreatedAt = now;
        agent.UpdatedAt = now;
        await agents.AddAsync(agent, cancellationToken);
        LogSeeded(logger, AgentConfig.InternalKey);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the built-in agent '{Key}'")]
    private static partial void LogSeeded(ILogger logger, string key);
}
