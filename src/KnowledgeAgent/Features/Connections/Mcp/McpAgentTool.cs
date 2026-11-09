using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KnowledgeAgent.Features.Connections.Mcp;

// API and MCP channels only get tools that run on their own, since nobody there can press Confirm.
internal sealed partial class McpToolSource(
    IMcpServerRepository servers,
    McpClientPool pool,
    IOptionsMonitor<McpOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<McpToolSource> logger) : IAgentToolSource
{
    public async Task<IReadOnlyList<IAgentTool>> ResolveAsync(AgentConfig agent, ChatCaller caller, CancellationToken cancellationToken)
    {
        var apps = agent.McpTools.Where(app => app.Enabled).ToList();
        if (apps.Count == 0)
        {
            return [];
        }

        var found = (await servers.GetManyAsync(apps.Select(app => app.ServerId).Distinct().ToList(), cancellationToken))
            .Where(server => server.Enabled)
            .ToDictionary(server => server.Id);
        var tools = new List<IAgentTool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (!found.TryGetValue(app.ServerId, out var server))
            {
                continue;
            }

            if (await pool.RefreshToolsAsync(server, cancellationToken) is { } fresh)
            {
                server.Tools = fresh.ToList();
                await SaveToolsAsync(server.Id, fresh, cancellationToken);
            }

            var (offered, missing) = app.Resolve(server.Tools);
            foreach (var name in missing)
            {
                LogToolSkipped(logger, name, server.Key, agent.Key);
            }

            foreach (var (info, policy, approval) in offered)
            {
                if (approval != McpToolApproval.Approved)
                {
                    LogNeedsApproval(logger, info.Name, server.Key, agent.Key, approval);
                    GuardTelemetry.Record(
                        Activity.Current,
                        approval == McpToolApproval.Changed ? GuardTelemetry.Reasons.McpDefinitionChanged : GuardTelemetry.Reasons.McpNotApproved,
                        McpToolNames.For(server.Key, info.Name));
                }

                if (Allows(policy, caller) && names.Add(McpToolNames.For(server.Key, info.Name)))
                {
                    tools.Add(new McpAgentTool(server, info, policy, pool, options.CurrentValue, McpAgentTool.SafetyOf(info, approval)));
                }
            }
        }

        return tools;
    }

    internal static bool Allows(McpToolPolicy policy, ChatCaller caller) => policy switch
    {
        McpToolPolicy.Auto => true,
        McpToolPolicy.Confirm => !caller.IsApi,
        McpToolPolicy.VerifiedOnly => !caller.IsApi && caller.IsVerified,
        _ => false,
    };

    // In a scope of its own, so the turn's database context keeps what it tracks; a failure only costs the cache.
    private async Task SaveToolsAsync(Guid serverId, IReadOnlyList<McpToolInfo> tools, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IMcpServerRepository>();
            if (await repository.GetAsync(serverId, cancellationToken) is not { } current)
            {
                return;
            }

            current.Tools = tools.ToList();
            current.LastCheckedAt = timeProvider.GetUtcNow();
            current.LastCheckOk = true;
            current.LastCheckError = null;
            await repository.UpdateAsync(current, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(logger, ex, serverId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped tool {Tool} of MCP server {Server} for agent {Agent}: the server no longer lists it")]
    private static partial void LogToolSkipped(ILogger logger, string tool, string server, string agent);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tool {Tool} of MCP server {Server} needs approval for agent {Agent} ({Approval}); it runs only after the person confirms")]
    private static partial void LogNeedsApproval(ILogger logger, string tool, string server, string agent, McpToolApproval approval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the refreshed tools of MCP server {ServerId} failed")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, Guid serverId);
}

internal sealed class McpAgentTool : IAgentTool
{
    internal const string WaitingSummary = "Waiting for confirmation";

    internal const string WaitingText =
        "This action has NOT run yet: it needs the person's confirmation first. Ask them in one short sentence whether to go "
        + "ahead and say exactly what it will do. They answer with the Confirm or Cancel button below your message. Do not "
        + "call this tool again for it.";

    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly McpServer _server;
    private readonly McpToolInfo _tool;
    private readonly McpToolPolicy _policy;
    private readonly McpClientPool _pool;
    private readonly McpOptions _options;
    private readonly McpCallSafety _safety;

    public McpAgentTool(
        McpServer server,
        McpToolInfo tool,
        McpToolPolicy policy,
        McpClientPool pool,
        McpOptions options,
        McpCallSafety safety = McpCallSafety.SideEffects)
    {
        _server = server;
        _tool = tool;
        _policy = policy;
        _pool = pool;
        _options = options;
        _safety = safety;
        Name = McpToolNames.For(server.Key, tool.Name);
        var description = $"{tool.Title ?? tool.Name}, from {server.Name}. {tool.Description}".Trim();
        if (NeedsConfirmation)
        {
            description += " The person must confirm before it runs.";
        }

        Declaration = AIFunctionFactory.CreateDeclaration(Name, description.Length > 1_024 ? description[..1_024] : description, Schema(tool.InputSchema));
    }

    public string Name { get; }

    public AIFunctionDeclaration Declaration { get; }

    public TimeSpan? Timeout => TimeSpan.FromSeconds(Math.Clamp(_options.CallTimeoutSeconds, 1, 600));

    public bool NeedsConfirmation => _policy is McpToolPolicy.Confirm or McpToolPolicy.VerifiedOnly;

    public string ServerName => _server.Name;

    // The hints are the server's claim, so they count only in the definition an admin approved.
    internal static McpCallSafety SafetyOf(McpToolInfo tool, McpToolApproval approval) =>
        approval == McpToolApproval.Approved && (tool.ReadOnlyHint == true || tool.IdempotentHint == true)
            ? McpCallSafety.RetrySafe
            : McpCallSafety.SideEffects;

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var turn = invocation.Turn;
        if (NeedsConfirmation && !invocation.Confirmed)
        {
            if (!turn.TryTakeBudget(TurnBudgets.McpConfirm, _options.MaxConfirmationsPerTurn))
            {
                return TurnBudgets.LimitReached(Name, TurnBudgets.McpConfirm);
            }

            return new ToolResult(WaitingText, WaitingSummary)
            {
                Confirmation = new ToolConfirmationPrompt(_tool.Title ?? _tool.Name, _server.Name)
                {
                    Details = McpConfirmationDetails.Describe(_tool.InputSchema, invocation.Arguments.Values),
                },
            };
        }

        if (!turn.TryTakeBudget(TurnBudgets.McpCall, _options.MaxCallsPerTurn))
        {
            return TurnBudgets.LimitReached(Name, TurnBudgets.McpCall);
        }

        if (!invocation.Confirmed && !turn.TryTakeBudget(TurnBudgets.McpAuto, _options.MaxAutoCallsPerTurn))
        {
            return TurnBudgets.LimitReached(Name, TurnBudgets.McpAuto);
        }

        var caller = _server.SendIdentity ? await McpCallerIdentity.ForAsync(turn.Caller, invocation.Services, cancellationToken) : null;
        CallToolResult result;
        try
        {
            result = await _pool.CallAsync(_server, _tool.Name, invocation.Arguments.Values, caller, _safety, IdempotencyKey(invocation), cancellationToken);
        }
        catch (McpProtocolException ex)
        {
            var message = $"{_server.Name} refused the call: {Shorten(ex.Message, 300)}";
            return ToolResult.Failure(message) with { LogText = message };
        }
        catch (McpConnectionException ex) when (ex.MayHaveRun && _safety == McpCallSafety.SideEffects)
        {
            var message = $"{_server.Name} did not confirm the result ({ex.Message}). The action may or may not have run: do not call it again. "
                + "Tell the person it is unclear and that they should check before trying again.";
            return ToolResult.Failure(message) with { LogText = ex.Message };
        }
        catch (McpConnectionException ex)
        {
            var message = $"{_server.Name} could not be reached ({ex.Message}). Tell the person it is not available right now.";
            return ToolResult.Failure(message) with { LogText = ex.Message };
        }

        var text = Render(result);
        var isError = result.IsError == true;
        var summary = isError
            ? $"{_server.Name} reported an error"
            : string.Create(CultureInfo.InvariantCulture, $"{_server.Name} answered ({text.Length:N0} characters)");
        return new ToolResult(isError ? "Error: " + text : text, summary, isError) { LogText = Shorten(text, Math.Max(100, _options.LoggedResultChars)), Untrusted = true };
    }

    // Stable across the retries of one call, and different for a call the model makes again.
    internal static string IdempotencyKey(ToolInvocation invocation) => $"{invocation.Turn.TurnId:N}:{invocation.CallId}";

    internal static string Render(CallToolResult result)
    {
        var builder = new StringBuilder();
        foreach (var block in result.Content ?? [])
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(block switch
            {
                TextContentBlock text => text.Text,
                ImageContentBlock => "[an image, not shown]",
                AudioContentBlock => "[an audio clip, not shown]",
                ResourceLinkBlock link => $"[a link: {link.Name} {link.Uri}]",
                EmbeddedResourceBlock embedded => embedded.Resource is TextResourceContents resource ? resource.Text : "[a file, not shown]",
                _ => "[content of type " + block.Type + ", not shown]",
            });
        }

        if (builder.Length == 0 && result.StructuredContent is { } structured)
        {
            builder.Append(JsonSerializer.Serialize(structured));
        }

        return builder.Length == 0 ? "(no content)" : builder.ToString();
    }

    private static JsonElement Schema(JsonElement? schema) =>
        schema is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object"
            ? value
            : EmptySchema;

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
