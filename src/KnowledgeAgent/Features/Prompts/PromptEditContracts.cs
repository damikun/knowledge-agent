using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Prompts;

public sealed record PromptVersionDto(
    int Version,
    string Type,
    string? Text,
    IReadOnlyList<PromptMessageDto> Messages,
    JsonObject? Config,
    IReadOnlyList<string> Labels,
    string? CommitMessage,
    bool Live);

public sealed record PromptVersionsDto(
    string Name,
    bool Editable,
    string Label,
    int? LiveVersion,
    int TotalVersions,
    IReadOnlyList<PromptVersionDto> Versions);

public sealed record SavePromptDraftRequest(
    string? Text = null,
    IReadOnlyList<PromptMessageDto>? Messages = null,
    string? CommitMessage = null,
    int? BaseVersion = null);

public sealed record PublishPromptRequest(int Version, int? ExpectedLiveVersion = null);

public sealed record PublishPromptResponse(string Name, string Label, int Version, int? PreviousVersion);
