using System.Security.Cryptography;
using System.Text.Json;

namespace KnowledgeAgent.Features.Connections.Mcp;

// SHA-256 of what the model sees of a tool and what decides how it runs: name, title, description, input schema and
// annotations. Object keys are sorted, so the same definition always gives the same value.
internal static class McpToolFingerprint
{
    public static string Compute(McpToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("inputSchema");
            if (tool.InputSchema is { } schema)
            {
                WriteCanonical(writer, schema);
            }
            else
            {
                writer.WriteNullValue();
            }

            WriteHint(writer, "destructiveHint", tool.DestructiveHint);
            WriteHint(writer, "idempotentHint", tool.IdempotentHint);
            writer.WriteString("name", tool.Name);
            WriteHint(writer, "openWorldHint", tool.OpenWorldHint);
            WriteHint(writer, "readOnlyHint", tool.ReadOnlyHint);
            writer.WriteString("title", tool.Title);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    public static bool Matches(McpToolInfo tool, string? fingerprint) =>
        fingerprint is not null && string.Equals(Compute(tool), fingerprint, StringComparison.Ordinal);

    private static void WriteHint(Utf8JsonWriter writer, string name, bool? value)
    {
        if (value is { } hint)
        {
            writer.WriteBoolean(name, hint);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
