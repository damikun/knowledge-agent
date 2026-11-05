using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.AI.Embeddings;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Help;

// Computed on the server because support users cannot read the admin APIs. Only counts and flags leave this class.
internal sealed partial class HelpSetupReader(
    IModelProfileResolver models,
    ISourceRepository sources,
    IAgentRepository agents,
    IWidgetRepository widgets,
    ILogger<HelpSetupReader> logger)
{
    public async Task<HelpSetupDto> ReadAsync(CancellationToken cancellationToken)
    {
        var (chat, embedding) = await ModelsAsync(cancellationToken);
        var stats = await sources.GetStatsAsync(cancellationToken);
        var agentList = await agents.ListAsync(cancellationToken);
        var widgetList = await widgets.ListAsync(cancellationToken);
        return new HelpSetupDto(
            chat,
            embedding,
            stats.CountByStatus.Values.Sum() > 0,
            agentList.Any(agent => !agent.IsBuiltIn),
            widgetList.Count > 0);
    }

    private async Task<(bool Chat, bool Embedding)> ModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await models.ListAsync(cancellationToken);
            var chat = profiles.FirstOrDefault(profile => string.Equals(profile.Name, AgentDefaults.ProfileName, StringComparison.OrdinalIgnoreCase));
            var embedding = profiles.FirstOrDefault(profile => string.Equals(profile.Name, EmbeddingService.ProfileName, StringComparison.OrdinalIgnoreCase));
            return (chat?.Effective is not null, embedding?.Effective is not null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogModelsUnreadable(logger, ex);
            return (false, false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Help: the model profiles could not be read")]
    private static partial void LogModelsUnreadable(ILogger logger, Exception exception);
}
