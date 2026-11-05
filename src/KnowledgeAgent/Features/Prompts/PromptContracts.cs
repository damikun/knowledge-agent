using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Models;

namespace KnowledgeAgent.Features.Prompts;

public sealed record PromptMessageDto(string Role, string Content);

public sealed record PromptSummaryDto(
    string Name,
    string Origin,
    int Version,
    string Type,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Variables,
    string? ExternalUrl);

public sealed record PromptDetailDto(
    string Name,
    string Origin,
    int Version,
    string Type,
    string? Text,
    IReadOnlyList<PromptMessageDto> Messages,
    JsonObject? Config,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Variables,
    string? ExternalUrl);

public sealed record RenderPromptRequest(IReadOnlyDictionary<string, JsonElement>? Variables = null, string? Label = null, int? Version = null);

public sealed record RenderedPromptDto(
    string Name,
    string Origin,
    int Version,
    string Type,
    string? Text,
    IReadOnlyList<PromptMessageDto> Messages,
    JsonObject? Config,
    string? Profile,
    ModelSettingsDto? ModelOverride);

public sealed record RenderTemplateRequest(string Template, IReadOnlyDictionary<string, JsonElement>? Variables = null);

public sealed record RenderTemplateResponse(string Output, IReadOnlyList<string> Variables);
