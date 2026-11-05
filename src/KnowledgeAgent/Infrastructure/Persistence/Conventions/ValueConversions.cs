using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KnowledgeAgent.Infrastructure.Persistence.Conventions;

// Npgsql only accepts UTC values for timestamptz, and converting to UTC keeps the instant.
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => value.ToUniversalTime(),
    value => value);

internal sealed class NulStrippingStringConverter() : ValueConverter<string, string>(
    value => StoreText.RemoveNul(value),
    value => value);

internal static class JsonColumns
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    public static readonly ValueConverter<string, string> JsonText = new(
        value => StoreText.RemoveNulFromJson(value),
        value => value);

    public static readonly ValueConverter<Dictionary<string, string>, string> StringDictionary = new(
        value => SerializeDictionary(value),
        json => DeserializeDictionary(json));

    public static readonly ValueComparer<Dictionary<string, string>> StringDictionaryComparer = new(
        (left, right) => DictionaryEquals(left, right),
        value => DictionaryHashCode(value),
        value => new Dictionary<string, string>(value, value.Comparer));

    public static readonly ValueConverter<List<Guid>, string> GuidList = new(
        value => JsonSerializer.Serialize(value, SerializerOptions),
        json => DeserializeGuidList(json));

    public static readonly ValueComparer<List<Guid>> GuidListComparer = new(
        (left, right) => ListEquals(left, right),
        value => ListHashCode(value),
        value => value.ToList());

    public static readonly ValueConverter<List<string>, string> StringList = new(
        value => JsonSerializer.Serialize(value.Select(StoreText.RemoveNul).ToList(), SerializerOptions),
        json => DeserializeStringList(json));

    public static readonly ValueComparer<List<string>> StringListComparer = new(
        (left, right) => ReferenceEquals(left, right) || (left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal)),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
        value => value.ToList());

    // A settings object stored as one JSON document; compared by content so changes inside lists are saved.
    public static ValueConverter<T, string> Document<T>() where T : class, new() => new(
        value => SerializeDocument(value),
        json => DeserializeDocument<T>(json));

    public static ValueComparer<T> DocumentComparer<T>() where T : class, new() => new(
        (left, right) => SerializeDocument(left) == SerializeDocument(right),
        value => SerializeDocument(value).GetHashCode(StringComparison.Ordinal),
        value => DeserializeDocument<T>(SerializeDocument(value)));

    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string SerializeDocument<T>(T? value) =>
        value is null ? "{}" : StoreText.RemoveNulFromJson(JsonSerializer.Serialize(value, DocumentOptions));

    private static T DeserializeDocument<T>(string json) where T : class, new() =>
        string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json, DocumentOptions) ?? new T();

    private static string SerializeDictionary(Dictionary<string, string> value)
    {
        var sanitized = new Dictionary<string, string>(value.Count, StringComparer.Ordinal);
        foreach (var (key, item) in value)
        {
            sanitized[StoreText.RemoveNul(key)] = StoreText.RemoveNul(item);
        }

        return JsonSerializer.Serialize(sanitized, SerializerOptions);
    }

    private static Dictionary<string, string> DeserializeDictionary(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions) ?? new Dictionary<string, string>();

    private static List<string> DeserializeStringList(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? [];

    private static List<Guid> DeserializeGuidList(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<Guid>>(json, SerializerOptions) ?? [];

    private static bool DictionaryEquals(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // XOR keeps the hash order-independent, matching dictionary equality.
    private static int DictionaryHashCode(Dictionary<string, string> value)
    {
        var hash = 0;
        foreach (var (key, item) in value)
        {
            hash ^= HashCode.Combine(key, item);
        }

        return hash;
    }

    private static bool ListEquals(List<Guid>? left, List<Guid>? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.SequenceEqual(right));

    private static int ListHashCode(List<Guid> value)
    {
        var hash = new HashCode();
        foreach (var item in value)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
