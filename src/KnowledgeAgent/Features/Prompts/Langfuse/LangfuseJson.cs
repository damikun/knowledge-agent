using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Prompts.Langfuse;

internal static class LangfuseJson
{
    public static PromptTemplate ParsePrompt(JsonNode? node)
    {
        if (node is not JsonObject prompt)
        {
            throw new LangfuseApiException("Langfuse returned an invalid prompt payload (expected a JSON object).");
        }

        var name = GetString(prompt, "name")
            ?? throw new LangfuseApiException("Langfuse returned a prompt without a name.");
        var body = prompt["prompt"];
        var isChat = body is JsonArray || ParseType(GetString(prompt, "type")) == PromptType.Chat;

        return new PromptTemplate
        {
            Name = name,
            Version = GetInt(prompt, "version") ?? 1,
            Type = isChat ? PromptType.Chat : PromptType.Text,
            Text = isChat ? null : ReadText(body),
            Messages = isChat ? ParseMessages(body) : [],
            Config = prompt["config"] is JsonObject config ? (JsonObject)config.DeepClone() : null,
            Labels = GetStrings(prompt, "labels"),
            Tags = GetStrings(prompt, "tags"),
            Origin = PromptOrigin.Langfuse,
            CommitMessage = GetString(prompt, "commitMessage"),
        };
    }

    public static LangfusePromptPage ParsePromptPage(JsonNode? node, int requestedPage, int requestedLimit)
    {
        if (node is not JsonObject root)
        {
            throw new LangfuseApiException("Langfuse returned an invalid prompt list (expected a JSON object).");
        }

        var items = new List<LangfusePromptListItem>();
        if (root["data"] is JsonArray data)
        {
            foreach (var item in data.OfType<JsonObject>())
            {
                if (GetString(item, "name") is not { } name)
                {
                    continue;
                }

                items.Add(new LangfusePromptListItem(
                    name,
                    ParseType(GetString(item, "type")),
                    GetInts(item, "versions"),
                    GetStrings(item, "labels"),
                    GetStrings(item, "tags"),
                    GetDate(item, "lastUpdatedAt"),
                    item["lastConfig"] is JsonObject config ? (JsonObject)config.DeepClone() : null));
            }
        }

        var meta = root["meta"] as JsonObject;
        return new LangfusePromptPage(
            items,
            GetInt(meta, "page") ?? requestedPage,
            GetInt(meta, "limit") ?? requestedLimit,
            GetInt(meta, "totalItems") ?? items.Count,
            Math.Max(1, GetInt(meta, "totalPages") ?? 1));
    }

    public static LangfuseHealth ParseHealth(JsonNode? node) => node is JsonObject health
        ? new LangfuseHealth(GetString(health, "status") ?? "unknown", GetString(health, "version"))
        : new LangfuseHealth("unknown", null);

    public static IReadOnlyList<LangfuseProject> ParseProjects(JsonNode? node)
    {
        if (node is not JsonObject { } root || root["data"] is not JsonArray data)
        {
            return [];
        }

        return data.OfType<JsonObject>()
            .Select(project => GetString(project, "id") is { Length: > 0 } id ? new LangfuseProject(id, GetString(project, "name")) : null)
            .OfType<LangfuseProject>()
            .ToList();
    }

    public static JsonObject BuildCreateBody(LangfuseCreatePromptRequest request)
    {
        JsonNode prompt = request.Type == PromptType.Chat
            ? new JsonArray(request.Messages
                .Select(message => (JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content })
                .ToArray())
            : JsonValue.Create(request.Text ?? string.Empty);

        var body = new JsonObject
        {
            ["name"] = request.Name,
            ["type"] = request.Type == PromptType.Chat ? "chat" : "text",
            ["prompt"] = prompt,
            ["config"] = request.Config?.DeepClone() ?? new JsonObject(),
            ["labels"] = ToJsonArray(request.Labels),
            ["tags"] = ToJsonArray(request.Tags),
        };

        if (!string.IsNullOrWhiteSpace(request.CommitMessage))
        {
            body["commitMessage"] = request.CommitMessage;
        }

        return body;
    }

    public static PromptType ParseType(string? type) =>
        string.Equals(type, "chat", StringComparison.OrdinalIgnoreCase) ? PromptType.Chat : PromptType.Text;

    private static IReadOnlyList<PromptMessage> ParseMessages(JsonNode? body)
    {
        if (body is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return [new PromptMessage("system", value.GetValue<string>())];
        }

        if (body is not JsonArray array)
        {
            return [];
        }

        var messages = new List<PromptMessage>(array.Count);
        foreach (var entry in array.OfType<JsonObject>())
        {
            // Placeholders are skipped because the application renders the whole conversation itself.
            if (string.Equals(GetString(entry, "type"), "placeholder", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (GetString(entry, "role") is { Length: > 0 } role)
            {
                messages.Add(new PromptMessage(role, ReadText(entry["content"])));
            }
        }

        return messages;
    }

    private static string ReadText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());

    private static string? GetString(JsonObject? obj, string key) =>
        obj?[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static int? GetInt(JsonObject? obj, string key) => obj?[key] is JsonValue value ? ToInt(value) : null;

    private static int? ToInt(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number when value.TryGetValue<int>(out var integer):
                return integer;
            case JsonValueKind.Number when value.TryGetValue<double>(out var number) && number is >= int.MinValue and <= int.MaxValue:
                return (int)number;
            case JsonValueKind.String when int.TryParse(value.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    private static IReadOnlyList<string> GetStrings(JsonObject obj, string key) => obj[key] is JsonArray array
        ? array.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>()).ToList()
        : [];

    private static IReadOnlyList<int> GetInts(JsonObject obj, string key) => obj[key] is JsonArray array
        ? array.OfType<JsonValue>().Select(ToInt).OfType<int>().ToList()
        : [];

    private static DateTimeOffset? GetDate(JsonObject obj, string key) =>
        GetString(obj, key) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
}
