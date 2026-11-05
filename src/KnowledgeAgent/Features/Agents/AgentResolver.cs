using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Sources.Folders;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Agents;

public sealed class AgentNotFoundException(string key) : Exception($"The agent '{key}' does not exist.")
{
    public string Key { get; } = key;
}

public interface IAgentResolver
{
    Task<AgentConfig> ResolveAsync(string? key, CancellationToken cancellationToken = default);

    Task<AgentConfig> ResolveAsync(Guid? id, string? fallbackKey, CancellationToken cancellationToken = default);

    Task<AgentConfig?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    // Never includes the Internal folder, which only the help assistant searches.
    Task<IReadOnlyList<Guid>?> ResolveScopeAsync(AgentConfig agent, IReadOnlyList<Guid> conversationScope, CancellationToken cancellationToken = default);
}

internal sealed class AgentResolver(
    IAgentRepository agents,
    ISourceFolderRepository folders,
    ISourceRepository sources,
    IOptions<AgentOptions> options) : IAgentResolver
{
    public async Task<AgentConfig> ResolveAsync(string? key, CancellationToken cancellationToken = default)
    {
        var wanted = string.IsNullOrWhiteSpace(key) ? AgentConfig.InternalKey : key.Trim().ToLowerInvariant();
        if (await agents.GetByKeyAsync(wanted, cancellationToken) is { } agent)
        {
            return agent;
        }

        // The seed may not have run yet.
        return wanted == AgentConfig.InternalKey ? Builtin(options.Value) : throw new AgentNotFoundException(wanted);
    }

    public async Task<AgentConfig> ResolveAsync(Guid? id, string? fallbackKey, CancellationToken cancellationToken = default) =>
        id is { } agentId && await agents.GetAsync(agentId, cancellationToken) is { } agent
            ? agent
            : await ResolveAsync(fallbackKey, cancellationToken);

    public Task<AgentConfig?> FindAsync(Guid id, CancellationToken cancellationToken = default) => agents.GetAsync(id, cancellationToken);

    public async Task<IReadOnlyList<Guid>?> ResolveScopeAsync(AgentConfig agent, IReadOnlyList<Guid> conversationScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var tree = new SourceFolderTree(await folders.ListAsync(cancellationToken));
        var internalFolders = tree.InternalFolderIds();
        IReadOnlyList<Guid>? allowed;
        if (agent.FolderIds.Count == 0)
        {
            allowed = await WithoutInternalAsync(internalFolders, cancellationToken);
        }
        else
        {
            var folderIds = agent.FolderIds
                .SelectMany(folderId => tree.SubtreeOf(folderId).Select(folder => folder.Id).Append(folderId))
                .ToHashSet();

            folderIds.ExceptWith(internalFolders);

            allowed = await sources.ListIdsInFoldersAsync(folderIds, cancellationToken);
        }

        if (allowed is null)
        {
            return conversationScope.Count == 0 ? null : conversationScope;
        }

        if (conversationScope.Count == 0)
        {
            return allowed;
        }

        var allowedSet = allowed.ToHashSet();
        return conversationScope.Where(allowedSet.Contains).ToList();
    }

    private async Task<IReadOnlyList<Guid>?> WithoutInternalAsync(IReadOnlySet<Guid> internalFolders, CancellationToken cancellationToken)
    {
        if (internalFolders.Count == 0 || (await sources.ListIdsInFoldersAsync(internalFolders, cancellationToken)).Count == 0)
        {
            return null;
        }

        return await sources.ListIdsOutsideFoldersAsync(internalFolders, cancellationToken);
    }

    internal static AgentConfig Builtin(AgentOptions settings) => new()
    {
        Key = AgentConfig.InternalKey,
        Name = settings.AppName,
        ProfileName = settings.OrchestratorProfile,
        Tools = AgentDefaults.Tools.Where(tool => settings.ResearcherEnabled || tool != ToolNames.DelegateResearch).ToList(),
        AllowGeneralKnowledge = settings.AllowGeneralKnowledge,

        HandoverPolicy = HandoverPolicy.Never,
        IsBuiltIn = true,
    };
}
