using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Agents.Access;

public sealed record AgentApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    int RequestsPerMinute,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record CreatedAgentApiKeyDto(AgentApiKeyDto Key, string Secret);

public sealed record CreateAgentApiKeyRequest(string Name, int? RequestsPerMinute = null);

public sealed record AgentAccessDto(bool McpServerEnabled, bool HttpApiEnabled, string McpPath, string ApiPath, int ActiveKeys);

public sealed record SaveAgentAccessRequest(bool? McpServerEnabled = null, bool? HttpApiEnabled = null);

internal sealed record AgentKeyMatch(AgentApiKey Key, AgentConfig Agent);

public interface IAgentApiKeyService
{
    Task<IReadOnlyList<AgentApiKeyDto>?> ListAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<CreatedAgentApiKeyDto?> CreateAsync(Guid agentId, CreateAgentApiKeyRequest request, string? createdBy, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid agentId, Guid keyId, CancellationToken cancellationToken = default);
    Task<AgentAccessDto?> GetAccessAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task<AgentAccessDto?> SaveAccessAsync(Guid agentId, SaveAgentAccessRequest request, CancellationToken cancellationToken = default);
}

internal sealed partial class AgentApiKeyService(
    KnowledgeDbContext db,
    DbContextGate gate,
    IAgentRepository agents,
    AgentKeyUsage usage,
    TimeProvider timeProvider,
    ILogger<AgentApiKeyService> logger) : IAgentApiKeyService
{
    public const string KeyPrefix = "ka_";
    public const int MaxActiveKeys = 20;

    public static string McpPath(string agentKey) => $"/mcp/agents/{agentKey}";

    public static string ApiPath(string agentKey) => $"/api/public/agents/{agentKey}";

    private const string BuiltInStaysInPortal = "The built-in agent cannot be used elsewhere. Create an agent of your own.";

    public static string HashOf(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task<IReadOnlyList<AgentApiKeyDto>?> ListAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        if (await agents.GetAsync(agentId, cancellationToken) is null)
        {
            return null;
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        var keys = await db.AgentApiKeys.AsNoTracking().Where(key => key.AgentId == agentId).ToListAsync(cancellationToken);
        return keys.OrderBy(key => key.RevokedAt.HasValue).ThenByDescending(key => key.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<CreatedAgentApiKeyDto?> CreateAsync(Guid agentId, CreateAgentApiKeyRequest request, string? createdBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await agents.GetAsync(agentId, cancellationToken) is not { } agent)
        {
            return null;
        }

        if (agent.IsBuiltIn)
        {
            throw new ValidationFailedException("id", BuiltInStaysInPortal);
        }

        var errors = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > AgentApiKey.MaxNameLength)
        {
            errors.Add("name", $"Name the key, in 1 to {AgentApiKey.MaxNameLength} characters, such as where it is used.");
        }

        var perMinute = request.RequestsPerMinute ?? AgentApiKey.DefaultRequestsPerMinute;
        if (perMinute is < 1 or > AgentApiKey.MaxRequestsPerMinute)
        {
            errors.Add("requestsPerMinute", $"Allow 1 to {AgentApiKey.MaxRequestsPerMinute} requests a minute.");
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        if (await db.AgentApiKeys.CountAsync(key => key.AgentId == agentId && key.RevokedAt == null, cancellationToken) >= MaxActiveKeys)
        {
            errors.Add("name", $"An agent can have at most {MaxActiveKeys} keys. Revoke one first.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        // 32 random bytes: a hash without salt is enough, since nobody can guess such a key from a list of hashes.
        var secret = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var apiKey = new AgentApiKey
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            Name = name,
            Prefix = secret[..9],
            Hash = HashOf(secret),
            RequestsPerMinute = perMinute,
            CreatedBy = createdBy is { Length: > 200 } ? createdBy[..200] : createdBy,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        try
        {
            db.AgentApiKeys.Add(apiKey);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        LogCreated(logger, apiKey.Id, agentId);
        return new CreatedAgentApiKeyDto(ToDto(apiKey), secret);
    }

    public async Task<bool> RevokeAsync(Guid agentId, Guid keyId, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var key = await db.AgentApiKeys.FirstOrDefaultAsync(item => item.Id == keyId && item.AgentId == agentId, cancellationToken);
            if (key is null)
            {
                return false;
            }

            key.RevokedAt ??= timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            usage.Forget(key.Hash);
            LogRevoked(logger, keyId, agentId);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<AgentAccessDto?> GetAccessAsync(Guid agentId, CancellationToken cancellationToken = default) =>
        await agents.GetAsync(agentId, cancellationToken) is { } agent ? await AccessOfAsync(agent, cancellationToken) : null;

    public async Task<AgentAccessDto?> SaveAccessAsync(Guid agentId, SaveAgentAccessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await agents.GetAsync(agentId, cancellationToken) is not { } agent)
        {
            return null;
        }

        if (agent.IsBuiltIn && (request.McpServerEnabled is true || request.HttpApiEnabled is true))
        {
            throw new ValidationFailedException("id", BuiltInStaysInPortal);
        }

        agent.McpServerEnabled = request.McpServerEnabled ?? agent.McpServerEnabled;
        agent.HttpApiEnabled = request.HttpApiEnabled ?? agent.HttpApiEnabled;
        agent.UpdatedAt = timeProvider.GetUtcNow();
        await agents.UpdateAsync(agent, cancellationToken);
        return await AccessOfAsync(agent, cancellationToken);
    }

    private async Task<AgentAccessDto> AccessOfAsync(AgentConfig agent, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var active = await db.AgentApiKeys.CountAsync(key => key.AgentId == agent.Id && key.RevokedAt == null, cancellationToken);
        return new AgentAccessDto(agent.McpServerEnabled, agent.HttpApiEnabled, McpPath(agent.Key), ApiPath(agent.Key), active);
    }

    private static AgentApiKeyDto ToDto(AgentApiKey key) =>
        new(key.Id, key.Name, key.Prefix, key.RequestsPerMinute, key.CreatedBy, key.CreatedAt, key.LastUsedAt, key.RevokedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created API key {KeyId} for agent {AgentId}")]
    private static partial void LogCreated(ILogger logger, Guid keyId, Guid agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Revoked API key {KeyId} of agent {AgentId}")]
    private static partial void LogRevoked(ILogger logger, Guid keyId, Guid agentId);
}

// LastUsedAt is written at most once a minute per key, so busy clients don't write on every request.
internal sealed class AgentKeyAuthenticator(KnowledgeDbContext db, DbContextGate gate, AgentKeyUsage usage, TimeProvider timeProvider)
{
    public async Task<AgentKeyMatch?> AuthenticateAsync(string presented, CancellationToken cancellationToken)
    {
        if (!presented.StartsWith(AgentApiKeyService.KeyPrefix, StringComparison.Ordinal) || presented.Length is < 20 or > 200)
        {
            return null;
        }

        var hash = AgentApiKeyService.HashOf(presented);
        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            var key = await db.AgentApiKeys.FirstOrDefaultAsync(item => item.Hash == hash, cancellationToken);

            // Compared again in constant time, so the lookup reveals nothing about near matches.
            if (key is null || key.RevokedAt is not null
                || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key.Hash), Encoding.ASCII.GetBytes(hash)))
            {
                return null;
            }

            // The built-in agent never answers here, whatever an old row says.
            var agent = (await db.Agents.AsNoTracking().FirstOrDefaultAsync(item => item.Id == key.AgentId, cancellationToken))?.ApplyBuiltInLimits();
            if (agent is null)
            {
                return null;
            }

            var now = timeProvider.GetUtcNow();
            if (usage.ShouldRecord(hash, now))
            {
                key.LastUsedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            return new AgentKeyMatch(key, agent);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}

internal sealed class AgentKeyUsage
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recorded = new(StringComparer.Ordinal);

    public bool ShouldRecord(string hash, DateTimeOffset now)
    {
        if (_recorded.TryGetValue(hash, out var last) && now - last < Interval)
        {
            return false;
        }

        _recorded[hash] = now;
        if (_recorded.Count > 10_000)
        {
            _recorded.Clear();
        }

        return true;
    }

    public void Forget(string hash) => _recorded.TryRemove(hash, out _);
}
