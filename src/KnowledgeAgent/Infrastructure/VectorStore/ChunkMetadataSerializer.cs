using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal static class ChunkMetadataSerializer
{
    public const string EmptyJson = "{}";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // Safe because metadata never ends up in HTML, and it keeps non-ASCII text readable in the database.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyDictionary<string, string> Empty { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public static string Serialize(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyJson;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in metadata)
            {
                if (key is not null && value is not null)
                {
                    writer.WriteString(key, value);
                }
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    public static IReadOnlyDictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Null or JsonValueKind.Undefined:
                        break;
                    case JsonValueKind.String:
                        values[property.Name] = property.Value.GetString()!;
                        break;
                    default:
                        values[property.Name] = property.Value.GetRawText();
                        break;
                }
            }

            return values.Count == 0 ? Empty : new ReadOnlyDictionary<string, string>(values);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    // Normalizes like a serialize and deserialize round trip, so in-memory results match stored ones.
    public static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return Empty;
        }

        var values = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            if (key is not null && value is not null)
            {
                values[key] = value;
            }
        }

        return values.Count == 0 ? Empty : new ReadOnlyDictionary<string, string>(values);
    }
}
