using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Search.Reranking;

internal sealed class LlmReranker(ChatModelHandle model, IPromptService prompts)
{
    public const int MaxRating = 3;
    internal const int MaxPassageChars = 1_500;

    public async Task<IReadOnlyList<double>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["query"] = query,
            ["passages"] = documents
                .Select((document, index) => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["number"] = index + 1,
                    ["text"] = Clip(document),
                })
                .ToList(),
        };

        var prompt = await prompts.RenderAsync(PromptNames.Rerank, variables, cancellationToken: cancellationToken);
        var options = model.CreateOptions();
        options.AdditionalProperties ??= [];
        options.AdditionalProperties[KnowledgeAgentTelemetry.LangfusePromptNameKey] = prompt.Name;
        options.AdditionalProperties[KnowledgeAgentTelemetry.LangfusePromptVersionKey] = prompt.Version;

        var response = await model.Client.GetResponseAsync(ToMessages(prompt), options, cancellationToken);
        return Parse(response.Text, documents.Count);
    }

    private static List<ChatMessage> ToMessages(RenderedPrompt prompt) =>
        prompt.Type == PromptType.Text || prompt.Messages.Count == 0
            ? [new ChatMessage(ChatRole.User, prompt.Text ?? string.Empty)]
            : prompt.Messages.Select(message => new ChatMessage(message.ToChatRole(), message.Content)).ToList();

    internal static IReadOnlyList<double> Parse(string? text, int count)
    {
        var start = text?.IndexOf('{') ?? -1;
        var end = text?.LastIndexOf('}') ?? -1;
        if (start < 0 || end <= start)
        {
            throw new FormatException("The reranking model did not reply with a JSON object.");
        }

        JsonObject ratings;
        try
        {
            ratings = JsonNode.Parse(text![start..(end + 1)]) as JsonObject ?? throw new FormatException("The reranking reply is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new FormatException("The reranking reply is not valid JSON: " + ex.Message, ex);
        }

        var scores = new double[count];
        var rated = 0;
        foreach (var (key, value) in ratings)
        {
            if (int.TryParse(key.Trim().Trim('[', ']'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number >= 1 && number <= count
                && value is JsonValue json && json.TryGetValue<double>(out var rating))
            {
                scores[number - 1] = Math.Clamp(rating, 0, MaxRating) / MaxRating;
                rated++;
            }
        }

        return rated > 0 ? scores : throw new FormatException("The reranking reply rated no passage.");
    }

    private static string Clip(string text) => text.Length <= MaxPassageChars ? text : text[..MaxPassageChars] + "…";
}
