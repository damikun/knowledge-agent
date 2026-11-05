using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Connections.Mcp;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Agents;

public interface IAgentUsage
{
    Task<IReadOnlyList<string>> ListUsesAsync(Guid agentId, CancellationToken cancellationToken = default);
}

public interface IAgentService
{
    Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<AgentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgentDto> CreateAsync(SaveAgentRequest request, CancellationToken cancellationToken = default);
    Task<AgentDto?> UpdateAsync(Guid id, SaveAgentRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, bool removeFromTeams, CancellationToken cancellationToken = default);
    AgentOptionsDto GetOptions();
}

internal sealed partial class AgentService(
    IAgentRepository agents,
    ISourceFolderRepository folders,
    IOptionsMonitor<AiOptions> aiOptions,
    IEnumerable<IAgentUsage> usages,
    TimeProvider timeProvider,
    ILogger<AgentService> logger,
    IMcpServerRepository? mcpServers = null) : IAgentService
{
    private const string BuiltInIsFixed = "The built-in agent is part of the product and cannot be changed or deleted.";

    public async Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await agents.ListAsync(cancellationToken)).Select(agent => agent.ToDto()).ToList();

    public async Task<AgentDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await agents.GetAsync(id, cancellationToken))?.ToDto();

    public async Task<AgentDto> CreateAsync(SaveAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = request.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var errors = new ValidationErrors();
        if (!KeyPattern().IsMatch(key))
        {
            errors.Add("key", "The key must be 1 to 64 lowercase letters, digits or dashes, starting with a letter or digit.");
        }
        else if (await agents.GetByKeyAsync(key, cancellationToken) is not null)
        {
            errors.Add("key", $"An agent with the key '{key}' already exists.");
        }

        var now = timeProvider.GetUtcNow();
        var agent = new AgentConfig { Id = Guid.CreateVersion7(), Key = key, CreatedAt = now };
        await ApplyAsync(agent, request, errors, cancellationToken);
        agent.UpdatedAt = now;

        var saved = await agents.AddAsync(agent, cancellationToken);
        LogCreated(logger, saved.Id, saved.Key);
        return saved.ToDto();
    }

    public async Task<AgentDto?> UpdateAsync(Guid id, SaveAgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await agents.GetAsync(id, cancellationToken) is not { } agent)
        {
            return null;
        }

        if (agent.IsBuiltIn)
        {
            throw new ValidationFailedException("id", BuiltInIsFixed);
        }

        var errors = new ValidationErrors();
        if (request.Key is { } key && !string.Equals(key.Trim(), agent.Key, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("key", "The key of an agent cannot change.");
        }

        await ApplyAsync(agent, request, errors, cancellationToken);
        agent.UpdatedAt = timeProvider.GetUtcNow();
        await agents.UpdateAsync(agent, cancellationToken);
        LogUpdated(logger, agent.Id, agent.Key);
        return agent.ToDto();
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => DeleteAsync(id, removeFromTeams: false, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, bool removeFromTeams, CancellationToken cancellationToken = default)
    {
        if (await agents.GetAsync(id, cancellationToken) is not { } agent)
        {
            return false;
        }

        if (agent.IsBuiltIn)
        {
            throw new ValidationFailedException("id", BuiltInIsFixed);
        }

        var uses = new List<string>();
        foreach (var usage in usages)
        {
            uses.AddRange(await usage.ListUsesAsync(id, cancellationToken));
        }

        if (uses.Count > 0)
        {
            throw new ValidationFailedException("id", $"The agent is used by {string.Join(", ", uses)}. Choose another agent there first.");
        }

        var teams = (await agents.ListAsync(cancellationToken))
            .Where(other => other.Id != id && other.Specialists.Any(specialist => specialist.AgentId == id))
            .ToList();
        if (teams.Count > 0 && !removeFromTeams)
        {
            throw new AgentIsSpecialistException(teams.Select(other => new AgentReferenceDto(other.Id, other.Name)).ToList());
        }

        foreach (var other in teams)
        {
            other.Specialists = other.Specialists.Where(specialist => specialist.AgentId != id).ToList();
            other.UpdatedAt = timeProvider.GetUtcNow();
            await agents.UpdateAsync(other, cancellationToken);
        }

        var deleted = await agents.DeleteAsync(id, cancellationToken);
        LogDeleted(logger, id, agent.Key);
        return deleted;
    }

    public AgentOptionsDto GetOptions() => new(AgentDefaults.Tools, ChatProfiles());

    private async Task ApplyAsync(AgentConfig agent, SaveAgentRequest request, ValidationErrors errors, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > AgentConfig.MaxNameLength)
        {
            errors.Add("name", $"The name must be 1 to {AgentConfig.MaxNameLength} characters long.");
        }

        var description = Optional(request.Description, AgentConfig.MaxDescriptionLength, "description", errors);
        var instructions = Optional(request.Instructions, AgentConfig.MaxInstructionsLength, "instructions", errors);
        var language = Optional(request.Language, AgentConfig.MaxLanguageLength, "language", errors);

        var profile = string.IsNullOrWhiteSpace(request.ProfileName) ? AgentDefaults.ProfileName : request.ProfileName.Trim();
        if (!ChatProfiles().Contains(profile, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("profileName", $"'{profile}' is not a chat profile. Choose one of: {string.Join(", ", ChatProfiles())}.");
        }

        // Older clients may still send request_human_handover as a tool.
        var tools = (request.Tools ?? AgentDefaults.Tools)
            .Select(tool => tool.Trim())
            .Where(tool => tool != ToolNames.RequestHumanHandover)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknownTools = tools.Where(tool => !AgentDefaults.Tools.Contains(tool, StringComparer.Ordinal)).ToList();
        if (unknownTools.Count > 0)
        {
            errors.Add("tools", $"Unknown tools: {string.Join(", ", unknownTools)}. Choose from: {string.Join(", ", AgentDefaults.Tools)}.");
        }

        var handoverPolicy = request.HandoverPolicy ?? agent.HandoverPolicy;
        if (!Enum.IsDefined(handoverPolicy))
        {
            errors.Add("handoverPolicy", "Choose never, onRequest or whenUnsure.");
        }

        var folderIds = (request.FolderIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (folderIds.Count > AgentConfig.MaxFolders)
        {
            errors.Add("folderIds", $"An agent can be limited to at most {AgentConfig.MaxFolders} folders.");
        }
        else if (folderIds.Count > 0)
        {
            var tree = new SourceFolderTree(await folders.ListAsync(cancellationToken));
            var missing = folderIds.Where(id => tree.Find(id) is null).ToList();
            if (missing.Count > 0)
            {
                errors.Add("folderIds", $"These folders do not exist: {string.Join(", ", missing)}.");
            }
            else if (folderIds.Any(tree.IsInternal))
            {
                errors.Add("folderIds", $"The {SourceFolder.InternalName} folder and its subfolders are only for the help assistant.");
            }
        }

        var mcpTools = request.McpApps is { } requested ? await McpAppsAsync(agent, requested, errors, cancellationToken) : agent.McpTools;
        var specialists = request.Specialists is { } team ? await SpecialistsAsync(agent, team, errors, cancellationToken) : agent.Specialists;

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        agent.McpTools = mcpTools;
        agent.Specialists = specialists;
        agent.ShowSpecialistName = request.ShowSpecialistName ?? agent.ShowSpecialistName;
        agent.Name = name;
        agent.Description = description;
        agent.Instructions = instructions;
        agent.Language = language;
        agent.ProfileName = profile;
        agent.Tools = tools.OrderBy(tool => AgentDefaults.Tools.ToList().IndexOf(tool)).ToList();
        agent.FolderIds = folderIds;
        agent.AllowGeneralKnowledge = request.AllowGeneralKnowledge;
        agent.HandoverPolicy = handoverPolicy;
    }

    // Picks the app no longer offers are kept and skipped at run time, so a renamed tool never blocks saving.
    private async Task<List<AgentMcpApp>> McpAppsAsync(AgentConfig agent, IReadOnlyList<AgentMcpAppDto> requested, ValidationErrors errors, CancellationToken cancellationToken)
    {
        var apps = requested.Where(app => app is not null && app.ServerId != Guid.Empty).ToList();
        if (apps.Count == 0)
        {
            return [];
        }

        if (mcpServers is null)
        {
            errors.Add("mcpApps", "MCP connections are not available on this server.");
            return [];
        }

        var servers = (await mcpServers.GetManyAsync(apps.Select(app => app.ServerId).Distinct().ToList(), cancellationToken)).ToDictionary(server => server.Id);
        var result = new List<AgentMcpApp>();
        foreach (var app in apps.DistinctBy(app => app.ServerId))
        {
            if (!servers.TryGetValue(app.ServerId, out var server))
            {
                errors.Add("mcpApps", $"The connected app '{app.ServerId}' does not exist.");
                continue;
            }

            if (!Enum.IsDefined(app.Mode))
            {
                errors.Add("mcpApps", $"Choose all or chosen tools for {server.Name}.");
                continue;
            }

            if (app.ChangesPolicy is not (McpToolPolicy.Confirm or McpToolPolicy.VerifiedOnly))
            {
                errors.Add("mcpApps", $"Tools of {server.Name} that make changes need confirm or verifiedOnly.");
                continue;
            }

            var picks = (app.Tools ?? []).Where(tool => tool is not null && !string.IsNullOrWhiteSpace(tool.Name)).DistinctBy(tool => tool.Name).ToList();
            if (picks.Count > AgentMcpApp.MaxToolsPerServer)
            {
                errors.Add("mcpApps", $"Pick at most {AgentMcpApp.MaxToolsPerServer} tools of {server.Name}.");
                continue;
            }

            var before = agent.McpTools.FirstOrDefault(saved => saved.ServerId == server.Id)?.Tools ?? [];
            var entry = new AgentMcpApp { ServerId = server.Id, Enabled = app.Enabled, Mode = app.Mode, ChangesPolicy = app.ChangesPolicy };
            foreach (var choice in picks)
            {
                var policy = choice.Policy;
                if (policy is { } given && !Enum.IsDefined(given))
                {
                    errors.Add("mcpApps", "Choose auto, confirm or verifiedOnly for each tool.");
                    continue;
                }

                if (server.Tools.FirstOrDefault(tool => tool.Name == choice.Name) is { } info)
                {
                    entry.Tools.Add(new AgentMcpTool { Name = info.Name, Policy = policy ?? entry.PolicyFor(info) });
                }
                else if (before.FirstOrDefault(saved => saved.Name == choice.Name) is { } kept)
                {
                    entry.Tools.Add(new AgentMcpTool { Name = kept.Name, Policy = policy ?? kept.Policy });
                }
                else
                {
                    errors.Add("mcpApps", $"{server.Name} has no tool '{choice.Name}'. Test the connection to refresh its tools.");
                }
            }

            result.Add(entry);
        }

        return result;
    }

    // Never the built-in agent (it may search every source), and no cycles.
    private async Task<List<AgentSpecialist>> SpecialistsAsync(AgentConfig agent, IReadOnlyList<AgentSpecialistDto> requested, ValidationErrors errors, CancellationToken cancellationToken)
    {
        var chosen = requested.Where(item => item is not null && item.AgentId != Guid.Empty).DistinctBy(item => item.AgentId).ToList();
        if (chosen.Count == 0)
        {
            return [];
        }

        if (chosen.Count > AgentConfig.MaxSpecialists)
        {
            errors.Add("specialists", $"An agent can hand off to at most {AgentConfig.MaxSpecialists} specialists.");
            return [];
        }

        var all = (await agents.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var result = new List<AgentSpecialist>();
        foreach (var item in chosen)
        {
            if (item.AgentId == agent.Id)
            {
                errors.Add("specialists", "An agent cannot hand off to itself.");
                continue;
            }

            if (!all.TryGetValue(item.AgentId, out var specialist))
            {
                errors.Add("specialists", $"The agent '{item.AgentId}' does not exist.");
                continue;
            }

            if (specialist.IsBuiltIn)
            {
                errors.Add("specialists", "The built-in agent cannot be a specialist: it may search every source.");
                continue;
            }

            var whenToUse = item.WhenToUse?.Trim() ?? string.Empty;
            if (whenToUse.Length is 0 or > AgentSpecialist.MaxWhenToUseLength)
            {
                errors.Add("specialists", $"Say when to hand off to {specialist.Name} in 1 to {AgentSpecialist.MaxWhenToUseLength} characters.");
                continue;
            }

            if (Reaches(all, specialist.Id, agent.Id))
            {
                errors.Add("specialists", $"{specialist.Name} already hands off to {agent.Name}, directly or through another agent. Remove one of the two.");
                continue;
            }

            result.Add(new AgentSpecialist { AgentId = specialist.Id, WhenToUse = whenToUse });
        }

        return result;
    }

    private static bool Reaches(IReadOnlyDictionary<Guid, AgentConfig> all, Guid start, Guid target)
    {
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>([start]);
        while (pending.TryPop(out var current))
        {
            if (current == target)
            {
                return true;
            }

            if (seen.Add(current) && all.TryGetValue(current, out var node))
            {
                foreach (var next in node.Specialists)
                {
                    pending.Push(next.AgentId);
                }
            }
        }

        return false;
    }

    private List<string> ChatProfiles() =>
        aiOptions.CurrentValue.Profiles
            .Where(profile => profile.Value.Kind == ModelKind.Chat)
            .Select(profile => profile.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? Optional(string? value, int maxLength, string field, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        if (trimmed is { Length: > 0 } && trimmed.Length > maxLength)
        {
            errors.Add(field, $"The {field} must be at most {maxLength} characters long.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex KeyPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created agent {AgentId} ({Key})")]
    private static partial void LogCreated(ILogger logger, Guid agentId, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated agent {AgentId} ({Key})")]
    private static partial void LogUpdated(ILogger logger, Guid agentId, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted agent {AgentId} ({Key})")]
    private static partial void LogDeleted(ILogger logger, Guid agentId, string key);
}
