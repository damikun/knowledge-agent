using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Sources.Ingestion;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KnowledgeAgent.Features.Connections.Mcp;

internal sealed class McpSecretProtector(IDataProtectionProvider dataProtection)
{
    // Changing the purpose makes every saved secret undecryptable.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("KnowledgeAgent.Connections.Mcp.Secrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    // Short secrets get no suffix so the hint never reveals a large part of them.
    public static string Hint(string secret) => secret.Length >= 12 ? "…" + secret[^4..] : "…";
}

internal sealed record McpProbe(bool Ok, string? Error, string? ServerName, string? ServerVersion, IReadOnlyList<McpToolInfo> Tools, long DurationMs);

// A failure talking to an MCP server, in words that are safe to show: never a header, token or stack trace.
internal sealed class McpConnectionException(string message, Exception? inner = null) : Exception(message, inner)
{
    // The request may have reached the server before the failure, so the tool may have run.
    public bool MayHaveRun { get; init; }
}

internal enum McpCallSafety
{
    // Sent again only when the first attempt surely never ran.
    SideEffects,

    // Approved as read-only or idempotent, so running it twice changes nothing.
    RetrySafe,
}

internal sealed partial class McpClientPool(
    McpSecretProtector secrets,
    IOptionsMonitor<IngestionOptions> ingestion,
    IOptionsMonitor<McpOptions> options,
    ILoggerFactory loggerFactory,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<Entry>>> _clients = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRefresh = new();
    private readonly ILogger _logger = loggerFactory.CreateLogger<McpClientPool>();

    // Sent in _meta with every attempt of one call, so a server that deduplicates can drop a repeat.
    public const string IdempotencyMetaKey = "knowledge-agent/idempotency-key";

    public Task<CallToolResult> CallAsync(McpServer server, string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken) =>
        CallAsync(server, toolName, arguments, caller: null, McpCallSafety.SideEffects, idempotencyKey: null, cancellationToken);

    public async Task<CallToolResult> CallAsync(
        McpServer server,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        JsonObject? caller,
        McpCallSafety safety,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = Request(caller, idempotencyKey);
        for (var attempt = 1; ; attempt++)
        {
            var entry = await GetAsync(server, cancellationToken);
            try
            {
                return await entry.Client.CallToolAsync(toolName, arguments, options: request, cancellationToken: cancellationToken);
            }
            catch (McpProtocolException)
            {
                // The server answered with an error; the connection itself is fine.
                throw;
            }
            catch (Exception ex) when (IsConnectionFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                await EvictAsync(server.Id, entry);

                // A timeout or a dropped connection after sending can mean the tool already ran: running an action
                // twice is worse than one error.
                var notRun = NeverRan(ex);
                if (attempt >= 2 || (safety != McpCallSafety.RetrySafe && !notRun))
                {
                    LogCallFailed(_logger, ex, server.Key, toolName);
                    throw new McpConnectionException(Describe(ex), ex) { MayHaveRun = !notRun };
                }
            }
        }
    }

    private static RequestOptions? Request(JsonObject? caller, string? idempotencyKey)
    {
        if (caller is null && string.IsNullOrEmpty(idempotencyKey))
        {
            return null;
        }

        var meta = new JsonObject();
        if (caller is not null)
        {
            meta[McpCallerIdentity.MetaKey] = caller.DeepClone();
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            meta[IdempotencyMetaKey] = idempotencyKey;
        }

        return new RequestOptions { Meta = meta };
    }

    // True only when the server surely did not run the request: the connection was never made, or the server
    // turned the request away with a client error (404 is how an expired MCP session is answered).
    internal static bool NeverRan(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException http)
            {
                return http.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
                        or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError
                    || http.StatusCode is { } status && (int)status is >= 400 and < 500 && (int)status != 408;
            }
        }

        return false;
    }

    public async Task<McpProbe> ProbeAsync(McpServer server, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        long Elapsed() => (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.ConnectTimeoutSeconds, 1, 120) * 2));
            await using var client = await ConnectAsync(server, timeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            return new McpProbe(true, null, client.ServerInfo?.Name, client.ServerInfo?.Version, ToInfos(tools.Select(tool => tool.ProtocolTool)), Elapsed());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpProbe(false, "The server did not answer in time.", null, null, [], Elapsed());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProbeFailed(_logger, ex, server.Key);
            return new McpProbe(false, Describe(ex), null, null, [], Elapsed());
        }
    }

    // At most once per server per interval, failures included, so a down server doesn't slow every turn.
    public async Task<IReadOnlyList<McpToolInfo>?> RefreshToolsAsync(McpServer server, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (settings.ToolListRefreshMinutes <= 0)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var maxAge = TimeSpan.FromMinutes(settings.ToolListRefreshMinutes);
        if (server.LastCheckedAt is { } checkedAt && now - checkedAt < maxAge)
        {
            return null;
        }

        var last = _lastRefresh.GetValueOrDefault(server.Id);
        if (now - last < maxAge || !_lastRefresh.TryUpdate(server.Id, now, last) && !_lastRefresh.TryAdd(server.Id, now))
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.ToolListRefreshTimeoutSeconds, 1, 60)));
            var entry = await GetAsync(server, timeout.Token);
            var tools = await entry.Client.ListToolsAsync(cancellationToken: timeout.Token);
            return ToInfos(tools.Select(tool => tool.ProtocolTool));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogRefreshFailed(_logger, ex, server.Key);
            return null;
        }
    }

    public async Task EvictAsync(Guid serverId)
    {
        if (_clients.TryRemove(serverId, out var lazy))
        {
            await DisposeEntryAsync(lazy);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (id, _) in _clients)
        {
            await EvictAsync(id);
        }
    }

    internal IReadOnlyList<McpToolInfo> ToInfos(IEnumerable<Tool> tools)
    {
        var settings = options.CurrentValue;
        var infos = new List<McpToolInfo>();
        foreach (var tool in tools)
        {
            if (infos.Count >= Math.Max(1, settings.MaxTools))
            {
                break;
            }

            var schema = tool.InputSchema;
            if (string.IsNullOrWhiteSpace(tool.Name) || tool.Name.Length > 128 || schema.GetRawText().Length > settings.MaxSchemaChars)
            {
                continue;
            }

            infos.Add(new McpToolInfo
            {
                Name = tool.Name,
                Title = Shorten(tool.Title ?? tool.Annotations?.Title, 200),
                Description = Shorten(tool.Description, 2_000),
                InputSchema = schema.Clone(),
                ReadOnlyHint = tool.Annotations?.ReadOnlyHint,
                DestructiveHint = tool.Annotations?.DestructiveHint,
                IdempotentHint = tool.Annotations?.IdempotentHint,
                OpenWorldHint = tool.Annotations?.OpenWorldHint,
            });
        }

        return infos;
    }

    private async Task<Entry> GetAsync(McpServer server, CancellationToken cancellationToken)
    {
        var stamp = Stamp(server);
        while (true)
        {
            var lazy = _clients.GetOrAdd(server.Id, _ => new Lazy<Task<Entry>>(() => OpenAsync(server, stamp)));
            Entry entry;
            try
            {
                entry = await lazy.Value.WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A failed connection is not kept, so the next call tries again.
                _clients.TryRemove(new KeyValuePair<Guid, Lazy<Task<Entry>>>(server.Id, lazy));
                LogConnectFailed(_logger, ex, server.Key);
                throw new McpConnectionException(Describe(ex), ex);
            }

            if (entry.Stamp == stamp)
            {
                return entry;
            }

            await EvictAsync(server.Id, entry);
        }
    }

    private async Task<Entry> OpenAsync(McpServer server, string stamp)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.ConnectTimeoutSeconds, 1, 120) * 2));
        var client = await ConnectAsync(server, timeout.Token);
        return new Entry(client, stamp);
    }

    private async Task<McpClient> ConnectAsync(McpServer server, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var endpoint))
        {
            throw new McpConnectionException("The server address is not a valid URL.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (server.AuthType != McpAuthType.None)
        {
            var secret = secrets.TryUnprotect(server.ProtectedSecret)
                ?? throw new McpConnectionException("The saved token cannot be read. Enter it again.");
            if (server.AuthType == McpAuthType.Bearer)
            {
                headers["Authorization"] = "Bearer " + secret;
            }
            else
            {
                headers[server.HeaderName!] = secret;
            }
        }

        var http = McpHttp.Create(ingestion.CurrentValue.AllowPrivateNetworks, settings, _logger);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = server.Key,
                ConnectionTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.ConnectTimeoutSeconds, 1, 120)),
                AdditionalHeaders = headers,
            },
            http,
            loggerFactory,
            ownsHttpClient: true);
        try
        {
            return await McpClient.CreateAsync(
                transport,
                new McpClientOptions { ClientInfo = new Implementation { Name = "knowledge-agent", Version = "1.0" } },
                loggerFactory,
                cancellationToken);
        }
        catch
        {
            await transport.DisposeAsync();
            throw;
        }
    }

    private async Task EvictAsync(Guid serverId, Entry entry)
    {
        if (_clients.TryGetValue(serverId, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully && ReferenceEquals(lazy.Value.Result, entry))
        {
            _clients.TryRemove(new KeyValuePair<Guid, Lazy<Task<Entry>>>(serverId, lazy));
        }

        await DisposeClientAsync(entry.Client);
    }

    private static async Task DisposeEntryAsync(Lazy<Task<Entry>> lazy)
    {
        if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            await DisposeClientAsync(lazy.Value.Result.Client);
        }
    }

    private static async Task DisposeClientAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine(ex);
        }
    }

    private static string Stamp(McpServer server) =>
        string.Create(CultureInfo.InvariantCulture, $"{server.UpdatedAt.UtcTicks}|{server.Url}|{server.AuthType}|{server.HeaderName}|{server.ProtectedSecret?.GetHashCode(StringComparison.Ordinal)}");

    private static bool IsConnectionFailure(Exception exception) =>
        exception is HttpRequestException or IOException or ObjectDisposedException or McpException or TimeoutException or InvalidOperationException;

    internal static string Describe(Exception exception) => exception switch
    {
        McpConnectionException connection => connection.Message,
        McpProtocolException protocol => "The server refused the request: " + Shorten(protocol.Message, 200),
        _ when Blocked(exception) => BlockedMessage,
        HttpRequestException { StatusCode: { } status } when (int)status is 401 or 403 =>
            string.Create(CultureInfo.InvariantCulture, $"The server refused the credentials (HTTP {(int)status}). Check the token."),
        HttpRequestException { StatusCode: { } status } =>
            string.Create(CultureInfo.InvariantCulture, $"The server answered with HTTP {(int)status}."),
        HttpRequestException => "The server could not be reached.",
        IOException io when io.Message.StartsWith("The MCP server sent more than", StringComparison.Ordinal) => io.Message,
        TimeoutException or OperationCanceledException => "The server did not answer in time.",
        _ => "The server could not be reached, or it does not speak MCP over Streamable HTTP.",
    };

    internal const string BlockedMessage =
        "The address points to a private or local network, which is not allowed. Use a public address, or allow private networks in the configuration.";

    private static bool Blocked(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Sources.Fetching.BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }

    private static string? Shorten(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text.Trim() : text[..max].Trim();

    private sealed record Entry(McpClient Client, string Stamp);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Calling tool {Tool} of MCP server {Server} failed")]
    private static partial void LogCallFailed(ILogger logger, Exception exception, string server, string tool);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting to MCP server {Server} failed")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, string server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshing the tools of MCP server {Server} failed; the cached list is used")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception, string server);

    [LoggerMessage(Level = LogLevel.Information, Message = "Testing MCP server {Server} failed")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception, string server);
}
