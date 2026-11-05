using System.Net;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Connections.Mcp;

public interface IMcpServerService
{
    Task<IReadOnlyList<McpServerDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<McpServerDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<McpServerDto> CreateAsync(SaveMcpServerRequest request, CancellationToken cancellationToken = default);
    Task<McpServerDto?> UpdateAsync(Guid id, SaveMcpServerRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<McpTestResultDto?> TestAsync(Guid id, CancellationToken cancellationToken = default);
    Task<McpTestResultDto> TestDraftAsync(SaveMcpServerRequest request, CancellationToken cancellationToken = default);
}

internal sealed partial class McpServerService(
    IMcpServerRepository servers,
    IAgentRepository agents,
    McpClientPool pool,
    McpSecretProtector secrets,
    IOptionsMonitor<IngestionOptions> ingestion,
    TimeProvider timeProvider,
    ILogger<McpServerService> logger) : IMcpServerService
{
    // Headers the transport sets itself, or that would change how the request is read.
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Content-Encoding", "Transfer-Encoding", "Connection", "Accept", "Accept-Encoding",
        "Cookie", "Mcp-Session-Id", "Mcp-Protocol-Version", "Last-Event-ID", "Upgrade", "Expect", "TE", "Trailer", "Proxy-Authorization",
    };

    public async Task<IReadOnlyList<McpServerDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await servers.ListAsync(cancellationToken);
        var allAgents = await agents.ListAsync(cancellationToken);
        return all.Select(server => server.ToDto(UsesOf(server, allAgents))).ToList();
    }

    public async Task<McpServerDto?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await servers.GetAsync(id, cancellationToken) is { } server
            ? server.ToDto(UsesOf(server, await agents.ListAsync(cancellationToken)))
            : null;

    public async Task<McpServerDto> CreateAsync(SaveMcpServerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var server = new McpServer { Id = Guid.CreateVersion7(), CreatedAt = now };
        Apply(server, request, isNew: true);
        server.Key = await UniqueKeyAsync(McpToolNames.KeyFromName(server.Name), cancellationToken);
        server.UpdatedAt = now;

        await RecordProbeAsync(server, cancellationToken);
        await servers.AddAsync(server, cancellationToken);
        LogCreated(logger, server.Id, server.Key);
        return server.ToDto([]);
    }

    public async Task<McpServerDto?> UpdateAsync(Guid id, SaveMcpServerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await servers.GetAsync(id, cancellationToken) is not { } server)
        {
            return null;
        }

        var before = (server.Url, server.AuthType, server.HeaderName, server.ProtectedSecret);
        Apply(server, request, isNew: false);
        server.UpdatedAt = timeProvider.GetUtcNow();
        if (before != (server.Url, server.AuthType, server.HeaderName, server.ProtectedSecret) && server.Enabled)
        {
            await RecordProbeAsync(server, cancellationToken);
        }

        await servers.UpdateAsync(server, cancellationToken);
        await pool.EvictAsync(server.Id);
        LogUpdated(logger, server.Id, server.Key);
        return server.ToDto(UsesOf(server, await agents.ListAsync(cancellationToken)));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await servers.GetAsync(id, cancellationToken) is not { } server)
        {
            return false;
        }

        foreach (var agent in await agents.ListAsync(cancellationToken))
        {
            if (agent.McpTools.RemoveAll(set => set.ServerId == id) > 0)
            {
                agent.UpdatedAt = timeProvider.GetUtcNow();
                await agents.UpdateAsync(agent, cancellationToken);
            }
        }

        var deleted = await servers.DeleteAsync(id, cancellationToken);
        await pool.EvictAsync(id);
        LogDeleted(logger, id, server.Key);
        return deleted;
    }

    public async Task<McpTestResultDto?> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await servers.GetAsync(id, cancellationToken) is not { } server)
        {
            return null;
        }

        var probe = await RecordProbeAsync(server, cancellationToken);
        await servers.UpdateAsync(server, cancellationToken);
        await pool.EvictAsync(server.Id);
        return ToResult(probe, server.Key);
    }

    public async Task<McpTestResultDto> TestDraftAsync(SaveMcpServerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = new McpServer { Id = Guid.NewGuid() };
        Apply(draft, request, isNew: true);
        draft.Key = McpToolNames.KeyFromName(draft.Name);
        return ToResult(await pool.ProbeAsync(draft, cancellationToken), draft.Key);
    }

    private McpTestResultDto ToResult(McpProbe probe, string key) => new(
        probe.Ok,
        probe.Ok ? $"Connected. The server offers {probe.Tools.Count} tool(s)." : probe.Error ?? "The server could not be reached.",
        probe.ServerName,
        probe.ServerVersion,
        probe.Tools.Select(tool => tool.ToDto(key)).ToList(),
        probe.DurationMs,
        timeProvider.GetUtcNow());

    // A failed check keeps the tools of the last good one, so agents keep their choices while a server is down.
    private async Task<McpProbe> RecordProbeAsync(McpServer server, CancellationToken cancellationToken)
    {
        var probe = await pool.ProbeAsync(server, cancellationToken);
        server.LastCheckedAt = timeProvider.GetUtcNow();
        server.LastCheckOk = probe.Ok;
        server.LastCheckError = probe.Error is { Length: > McpServer.MaxErrorLength } error ? error[..McpServer.MaxErrorLength] : probe.Error;
        if (probe.Ok)
        {
            server.Tools = probe.Tools.ToList();
            server.ServerName = Shorten(probe.ServerName, 200);
            server.ServerVersion = Shorten(probe.ServerVersion, 100);
        }

        return probe;
    }

    private void Apply(McpServer server, SaveMcpServerRequest request, bool isNew)
    {
        var errors = new ValidationErrors();
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > McpServer.MaxNameLength)
        {
            errors.Add("name", $"The name must be 1 to {McpServer.MaxNameLength} characters long.");
        }

        var url = request.Url?.Trim() ?? string.Empty;
        var authType = request.AuthType ?? McpAuthType.None;
        if (!Enum.IsDefined(authType))
        {
            errors.Add("authType", "Choose none, bearer or header.");
        }

        ValidateUrl(url, authType, errors);

        string? headerName = null;
        if (authType == McpAuthType.Header)
        {
            headerName = request.HeaderName?.Trim();
            if (string.IsNullOrEmpty(headerName) || !HeaderPattern().IsMatch(headerName))
            {
                errors.Add("headerName", "Enter the header name, such as X-Api-Key: letters, digits and dashes.");
            }
            else if (ReservedHeaders.Contains(headerName) || headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("headerName", headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    ? "For the Authorization header, choose Bearer token."
                    : $"The {headerName} header cannot be set.");
            }
        }

        var secret = request.Secret?.Trim();
        if (secret is { Length: > McpServer.MaxSecretLength })
        {
            errors.Add("secret", $"The token must be at most {McpServer.MaxSecretLength} characters long.");
        }
        else if (secret is not null && secret.Any(char.IsControl))
        {
            errors.Add("secret", "The token cannot contain line breaks or other control characters.");
        }

        var keepsSecret = secret is null && !isNew && server.ProtectedSecret is not null;
        if (authType != McpAuthType.None && string.IsNullOrEmpty(secret) && !keepsSecret)
        {
            errors.Add("secret", "Enter the token.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        server.Name = name;
        server.Url = url;
        server.AuthType = authType;
        server.HeaderName = headerName;
        server.Enabled = request.Enabled ?? server.Enabled;
        if (authType == McpAuthType.None || secret == string.Empty)
        {
            server.ProtectedSecret = null;
            server.SecretHint = null;
        }
        else if (!string.IsNullOrEmpty(secret))
        {
            server.ProtectedSecret = secrets.Protect(secret);
            server.SecretHint = McpSecretProtector.Hint(secret);
        }
    }

    private void ValidateUrl(string url, McpAuthType authType, ValidationErrors errors)
    {
        var allowPrivate = ingestion.CurrentValue.AllowPrivateNetworks;
        if (url.Length is 0 or > McpServer.MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("url", "Enter the server's address, such as https://mcp.example.com/mcp.");
            return;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("url", "Put the credentials under Authentication, not in the address.");
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && authType != McpAuthType.None && !allowPrivate)
        {
            // A token sent over plain http can be read on the way.
            errors.Add("url", "Use an https address when the server needs a token.");
        }
        else if (!allowPrivate && (uri.IsLoopback || (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) && !NetworkAddressPolicy.IsPublic(address))))
        {
            errors.Add("url", McpClientPool.BlockedMessage);
        }
    }

    private async Task<string> UniqueKeyAsync(string baseKey, CancellationToken cancellationToken)
    {
        var key = baseKey;
        for (var suffix = 2; await servers.KeyExistsAsync(key, cancellationToken); suffix++)
        {
            var tail = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            key = (baseKey.Length + tail.Length > McpServer.MaxKeyLength ? baseKey[..(McpServer.MaxKeyLength - tail.Length)] : baseKey) + tail;
        }

        return key;
    }

    private static List<McpServerUseDto> UsesOf(McpServer server, IReadOnlyList<AgentConfig> allAgents) =>
        allAgents
            .Select(agent => (agent, app: agent.McpTools.FirstOrDefault(app => app.ServerId == server.Id)))
            .Where(item => item.app is { Enabled: true } && (item.app.Mode == McpAppMode.All || item.app.Tools.Count > 0))
            .Select(item => new McpServerUseDto(item.agent.Id, item.agent.Name, item.app!.Resolve(server.Tools).Tools.Count, item.app.Mode))
            .ToList();

    private static string? Shorten(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    [GeneratedRegex("^[A-Za-z0-9-]{1,100}$")]
    private static partial Regex HeaderPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created MCP server {ServerId} ({Key})")]
    private static partial void LogCreated(ILogger logger, Guid serverId, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated MCP server {ServerId} ({Key})")]
    private static partial void LogUpdated(ILogger logger, Guid serverId, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted MCP server {ServerId} ({Key})")]
    private static partial void LogDeleted(ILogger logger, Guid serverId, string key);
}
