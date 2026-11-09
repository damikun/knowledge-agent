using System.Globalization;
using System.Text.Json;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;

namespace KnowledgeAgent.Features.Connections.Mcp;

// The arguments an action will run with, as "name: value" lines built here rather than described by the model,
// so the Confirm button shows exactly what runs.
internal static class McpConfirmationDetails
{
    public const int MaxDetails = 20;
    public const int MaxValueLength = 300;

    public static IReadOnlyList<ConfirmationDetailDto> Describe(JsonElement? schema, IReadOnlyDictionary<string, object?> arguments)
    {
        var order = new List<string>();
        if (schema is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            order.AddRange(properties.EnumerateObject().Select(property => property.Name).Where(arguments.ContainsKey));
        }

        order.AddRange(arguments.Keys.Where(key => !order.Contains(key, StringComparer.Ordinal)).Order(StringComparer.Ordinal));

        var details = order.Take(MaxDetails)
            .Select(name => new ConfirmationDetailDto(Clip(name, 80), Clip(UntrustedText.StripInvisible(Render(arguments[name])), MaxValueLength)))
            .ToList();
        if (order.Count > MaxDetails)
        {
            details.Add(new ConfirmationDetailDto("…", string.Create(CultureInfo.InvariantCulture, $"{order.Count - MaxDetails} more")));
        }

        return details;
    }

    internal static string Render(object? value) => value switch
    {
        null => "none",
        JsonElement element => Render(element),
        string text => text,
        bool flag => flag ? "yes" : "no",
        _ => Render(JsonSerializer.SerializeToElement(value)),
    };

    private static string Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        JsonValueKind.Null or JsonValueKind.Undefined => "none",
        JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Object ? item.GetRawText() : Render(item))),
        _ => element.GetRawText(),
    };

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
