using System.Text.Json;
using KnowledgeAgent.Features.Agents;

namespace KnowledgeAgent.Features.Connections.Mcp;

public sealed record McpServerDto(
    Guid Id,
    string Key,
    string Name,
    string Url,
    McpAuthType AuthType,
    string? HeaderName,
    bool HasSecret,
    string? SecretHint,
    bool Enabled,
    McpCheckDto? LastCheck,
    string? ServerName,
    string? ServerVersion,
    IReadOnlyList<McpToolDto> Tools,
    IReadOnlyList<McpServerUseDto> UsedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record McpCheckDto(DateTimeOffset CheckedAt, bool Ok, string? Error);

public sealed record McpToolDto(
    string Name,
    string ModelName,
    string? Title,
    string? Description,
    JsonElement? InputSchema,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent,
    bool? OpenWorld,
    McpToolPolicy DefaultPolicy);

public sealed record McpServerUseDto(Guid AgentId, string AgentName, int ToolCount, McpAppMode Mode = McpAppMode.All);

// Secret: null keeps the saved one, an empty string removes it. AuthType defaults to none.
public sealed record SaveMcpServerRequest(
    string Name,
    string Url,
    McpAuthType? AuthType = null,
    string? HeaderName = null,
    string? Secret = null,
    bool? Enabled = null);

public sealed record McpTestResultDto(
    bool Ok,
    string Message,
    string? ServerName,
    string? ServerVersion,
    IReadOnlyList<McpToolDto> Tools,
    long DurationMs,
    DateTimeOffset CheckedAt);

internal static class McpServerMappings
{
    public static McpToolDto ToDto(this McpToolInfo tool, string serverKey) => new(
        tool.Name,
        McpToolNames.For(serverKey, tool.Name),
        tool.Title,
        tool.Description,
        tool.InputSchema,
        tool.ReadOnlyHint,
        tool.DestructiveHint,
        tool.IdempotentHint,
        tool.OpenWorldHint,
        DefaultPolicy(tool));

    public static McpToolPolicy DefaultPolicy(McpToolInfo tool) => tool.MayChangeThings ? McpToolPolicy.Confirm : McpToolPolicy.Auto;

    public static McpServerDto ToDto(this McpServer server, IReadOnlyList<McpServerUseDto> usedBy) => new(
        server.Id,
        server.Key,
        server.Name,
        server.Url,
        server.AuthType,
        server.HeaderName,
        server.ProtectedSecret is not null,
        server.SecretHint,
        server.Enabled,
        server.LastCheckedAt is { } checkedAt ? new McpCheckDto(checkedAt, server.LastCheckOk == true, server.LastCheckError) : null,
        server.ServerName,
        server.ServerVersion,
        server.Tools.Select(tool => tool.ToDto(server.Key)).ToList(),
        usedBy,
        server.CreatedAt,
        server.UpdatedAt);
}
