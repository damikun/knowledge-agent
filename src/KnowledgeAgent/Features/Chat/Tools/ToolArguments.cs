using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class ToolArguments
{
    private static readonly char[] ListSeparators = [',', ';', ' ', '\n', '\r', '\t'];
    private static readonly JsonSerializerOptions CompactJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    // Collected and reported together, so the model can fix every problem in one retry.
    private readonly List<string> _errors = [];

    public ToolArguments(IDictionary<string, object?>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var (key, value) in values)
        {
            _values[key] = value;
        }
    }

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyDictionary<string, object?> Values => _values;

    public string? GetString(string name, bool required = false)
    {
        if (!TryGetValue(name, out var value))
        {
            if (required)
            {
                _errors.Add($"'{name}' is required.");
            }

            return null;
        }

        var text = value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            Guid guid => guid.ToString(),
            _ => null,
        };

        if (text is null)
        {
            _errors.Add($"'{name}' must be a string.");
            return null;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            if (required)
            {
                _errors.Add($"'{name}' must not be empty.");
            }

            return null;
        }

        return text;
    }

    public int? GetInt32(string name)
    {
        if (!TryGetValue(name, out var value))
        {
            return null;
        }

        if (TryConvertInt32(value, out var number))
        {
            return number;
        }

        _errors.Add($"'{name}' must be an integer.");
        return null;
    }

    public bool? GetBoolean(string name)
    {
        if (!TryGetValue(name, out var value))
        {
            return null;
        }

        bool? flag = value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string s when bool.TryParse(s.Trim(), out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString()?.Trim(), out var parsed) => parsed,
            _ => null,
        };

        if (flag is null)
        {
            _errors.Add($"'{name}' must be true or false.");
        }

        return flag;
    }

    public IReadOnlyList<string> GetStringList(string name)
    {
        if (!TryGetValue(name, out var value))
        {
            return [];
        }

        switch (value)
        {
            case string text:
                return Split(text);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return Split(element.GetString() ?? string.Empty);
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                return element.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
            case IEnumerable items:
                return items.Cast<object?>()
                    .Select(item => item switch
                    {
                        string s => s,
                        Guid guid => guid.ToString(),
                        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                        _ => null,
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .ToList();
            default:
                _errors.Add($"'{name}' must be an array of strings.");
                return [];
        }
    }

    public IReadOnlyList<Guid> GetGuidList(string name) =>
        GetStringList(name)
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

    public void ThrowIfInvalid(string toolName)
    {
        if (_errors.Count > 0)
        {
            throw new ToolArgumentException($"Invalid arguments for {toolName}: {string.Join(" ", _errors)}");
        }
    }

    public static JsonElement ToJson(IDictionary<string, object?>? arguments)
    {
        try
        {
            return JsonSerializer.SerializeToElement(arguments ?? new Dictionary<string, object?>(), CompactJson);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private bool TryGetValue(string name, out object? value) =>
        _values.TryGetValue(name, out value)
        && value is not null
        && value is not JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };

    private static bool TryConvertInt32(object? value, out int number)
    {
        number = 0;
        switch (value)
        {
            case int i:
                number = i;
                return true;
            case long or short or byte or sbyte or ushort or uint or ulong:
                return TryFromDecimal(Convert.ToDecimal(value, CultureInfo.InvariantCulture), out number);
            case double d:
                return TryFromDouble(d, out number);
            case float f:
                return TryFromDouble(f, out number);
            case decimal m:
                return TryFromDecimal(m, out number);
            case string s:
                return TryParse(s, out number);
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                if (element.TryGetInt32(out number))
                {
                    return true;
                }

                return element.TryGetDouble(out var parsed) && TryFromDouble(parsed, out number);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return TryParse(element.GetString() ?? string.Empty, out number);
            default:
                return false;
        }
    }

    private static bool TryParse(string text, out int number)
    {
        number = 0;
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && TryFromDouble(parsed, out number);
    }

    private static bool TryFromDouble(double value, out int number)
    {
        number = 0;
        if (double.IsFinite(value) && Math.Abs(value % 1) < double.Epsilon && value is >= int.MinValue and <= int.MaxValue)
        {
            number = (int)value;
            return true;
        }

        return false;
    }

    private static bool TryFromDecimal(decimal value, out int number)
    {
        number = 0;
        if (decimal.Truncate(value) == value && value is >= int.MinValue and <= int.MaxValue)
        {
            number = (int)value;
            return true;
        }

        return false;
    }

    private static List<string> Split(string text) =>
        text.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
