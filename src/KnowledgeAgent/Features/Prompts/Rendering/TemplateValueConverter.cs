using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Prompts.Rendering;

// Keys are case-insensitive so that PascalCase C# properties work with camelCase templates.
internal static class TemplateValueConverter
{
    internal const int MaxDepth = 64;

    private static readonly ConcurrentDictionary<Type, MemberAccessor[]> MemberAccessors = new();
    private static readonly ConcurrentDictionary<Type, bool> StringKeyedPairSequences = new();

    public static Dictionary<string, object?> ConvertVariables(IReadOnlyDictionary<string, object?> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = new Dictionary<string, object?>(variables.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in variables)
        {
            result[key] = Convert(value, 1, visited);
        }

        return result;
    }

    public static object? Convert(object? value) => Convert(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static object? Convert(object? value, int depth, HashSet<object> visited)
    {
        switch (value)
        {
            case null:
                return null;
            case string or bool or char:
                return value;
            case JsonElement element:
                return ConvertElement(element, depth);
            case JsonDocument document:
                return ConvertElement(document.RootElement, depth);
            case JsonNode node:
                return ConvertNode(node, depth);
        }

        var type = value.GetType();
        if (IsScalar(value, type))
        {
            return value;
        }

        EnsureDepth(depth);

        // Reference cycles are cut instead of recursing forever.
        if (!type.IsValueType && !visited.Add(value))
        {
            return null;
        }

        try
        {
            return value switch
            {
                IDictionary dictionary => ConvertDictionary(dictionary, depth, visited),
                IEnumerable<KeyValuePair<string, object?>> pairs => ConvertPairs(pairs, depth, visited),
                IEnumerable sequence when IsStringKeyedPairSequence(type) => ConvertPairSequence(sequence, depth, visited),
                IEnumerable sequence => ConvertSequence(sequence, depth, visited),
                _ => ConvertObject(value, type, depth, visited),
            };
        }
        finally
        {
            if (!type.IsValueType)
            {
                visited.Remove(value);
            }
        }
    }

    private static bool IsScalar(object value, Type type)
    {
        if (value is IConvertible or IFormattable or Uri)
        {
            return true;
        }

        if (value is IEnumerable || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)))
        {
            return false;
        }

        // Other framework types render via ToString() because reflecting over them produces huge or meaningless graphs.
        var ns = type.Namespace;
        return ns is not null && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));
    }

    private static void EnsureDepth(int depth)
    {
        if (depth >= MaxDepth)
        {
            throw new PromptRenderException($"Template variables are nested deeper than {MaxDepth} levels.");
        }
    }

    private static object? ConvertElement(JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    return integer;
                }

                return element.TryGetDouble(out var number) ? number : element.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Object:
            {
                EnsureDepth(depth);
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    result[property.Name] = ConvertElement(property.Value, depth + 1);
                }

                return result;
            }
            case JsonValueKind.Array:
            {
                EnsureDepth(depth);
                var result = new List<object?>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    result.Add(ConvertElement(item, depth + 1));
                }

                return result;
            }
            default:
                return null;
        }
    }

    private static object? ConvertNode(JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                EnsureDepth(depth);
                var result = new Dictionary<string, object?>(obj.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var (key, child) in obj)
                {
                    result[key] = ConvertNode(child, depth + 1);
                }

                return result;
            }
            case JsonArray array:
            {
                EnsureDepth(depth);
                var result = new List<object?>(array.Count);
                foreach (var child in array)
                {
                    result.Add(ConvertNode(child, depth + 1));
                }

                return result;
            }
            case JsonValue value when value.TryGetValue<JsonElement>(out var element):
                return ConvertElement(element, depth);
            case JsonValue value when value.TryGetValue<string>(out var text):
                return text;
            default:
            {
                // Values created from CLR objects round-trip through JSON so that the result is uniform.
                using var document = JsonDocument.Parse(node.ToJsonString());
                return ConvertElement(document.RootElement, depth);
            }
        }
    }

    private static Dictionary<string, object?> ConvertDictionary(IDictionary dictionary, int depth, HashSet<object> visited)
    {
        var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (System.Convert.ToString(entry.Key, CultureInfo.InvariantCulture) is { } key)
            {
                result[key] = Convert(entry.Value, depth + 1, visited);
            }
        }

        return result;
    }

    private static Dictionary<string, object?> ConvertPairs(IEnumerable<KeyValuePair<string, object?>> pairs, int depth, HashSet<object> visited)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            result[key] = Convert(value, depth + 1, visited);
        }

        return result;
    }

    private static Dictionary<string, object?> ConvertPairSequence(IEnumerable sequence, int depth, HashSet<object> visited)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sequence)
        {
            if (item is null)
            {
                continue;
            }

            var accessors = MemberAccessors.GetOrAdd(item.GetType(), CreateAccessors);
            var key = accessors.FirstOrDefault(a => a.Name == "Key")?.Get(item) as string;
            if (key is not null)
            {
                result[key] = Convert(accessors.FirstOrDefault(a => a.Name == "Value")?.Get(item), depth + 1, visited);
            }
        }

        return result;
    }

    private static List<object?> ConvertSequence(IEnumerable sequence, int depth, HashSet<object> visited)
    {
        var result = sequence is ICollection collection ? new List<object?>(collection.Count) : new List<object?>();
        foreach (var item in sequence)
        {
            result.Add(Convert(item, depth + 1, visited));
        }

        return result;
    }

    private static Dictionary<string, object?> ConvertObject(object value, Type type, int depth, HashSet<object> visited)
    {
        var accessors = MemberAccessors.GetOrAdd(type, CreateAccessors);
        var result = new Dictionary<string, object?>(accessors.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var accessor in accessors)
        {
            object? member;
            try
            {
                member = accessor.Get(value);
            }
            catch (TargetInvocationException)
            {
                // A throwing getter must not break rendering, so the member renders as missing.
                continue;
            }

            result[accessor.Name] = Convert(member, depth + 1, visited);
        }

        return result;
    }

    private static bool IsStringKeyedPairSequence(Type type) =>
        StringKeyedPairSequences.GetOrAdd(type, static t => t.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            && i.GetGenericArguments()[0] is { IsGenericType: true } pair
            && pair.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            && pair.GetGenericArguments()[0] == typeof(string)));

    private static MemberAccessor[] CreateAccessors(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .Select(p => new MemberAccessor(p.Name, p.GetValue));
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => new MemberAccessor(f.Name, f.GetValue));
        return properties.Concat(fields).ToArray();
    }

    private sealed record MemberAccessor(string Name, Func<object, object?> Get);
}
