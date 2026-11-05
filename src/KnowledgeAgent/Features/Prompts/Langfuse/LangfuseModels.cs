using System.Net;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Prompts.Langfuse;

internal sealed record LangfusePromptListItem(
    string Name,
    PromptType Type,
    IReadOnlyList<int> Versions,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Tags,
    DateTimeOffset? LastUpdatedAt,
    JsonObject? LastConfig);

internal sealed record LangfusePromptPage(
    IReadOnlyList<LangfusePromptListItem> Items,
    int Page,
    int Limit,
    int TotalItems,
    int TotalPages);

internal sealed record LangfuseCreatePromptRequest
{
    public required string Name { get; init; }
    public PromptType Type { get; init; } = PromptType.Text;
    public string? Text { get; init; }
    public IReadOnlyList<PromptMessage> Messages { get; init; } = [];
    public JsonObject? Config { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? CommitMessage { get; init; }

    public static LangfuseCreatePromptRequest FromTemplate(PromptTemplate template, IReadOnlyList<string> labels, string? commitMessage) => new()
    {
        Name = template.Name,
        Type = template.Type,
        Text = template.Text,
        Messages = template.Messages,
        Config = template.Config,
        Labels = labels,
        Tags = template.Tags,
        CommitMessage = commitMessage,
    };
}

internal sealed record LangfuseHealth(string Status, string? Version);

internal sealed record LangfuseProject(string Id, string? Name);

internal sealed class LangfuseApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
