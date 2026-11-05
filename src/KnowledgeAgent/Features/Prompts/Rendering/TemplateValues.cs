using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using HandlebarsDotNet;

namespace KnowledgeAgent.Features.Prompts.Rendering;

internal static class TemplateValues
{
    internal static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static readonly JsonSerializerOptions IndentedJson = new(CompactJson) { WriteIndented = true };

    // Handlebars passes UndefinedBindingResult for missing variables.
    public static object? Unwrap(object? value) => value is UndefinedBindingResult ? null : value;

    public static bool IsTruthy(object? value) => HandlebarsUtils.IsTruthyOrNonEmpty(Unwrap(value));

    public static bool IsMissing(object? value) => Unwrap(value) switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        IEnumerable sequence => !sequence.GetEnumerator().MoveNext(),
        _ => false,
    };

    public static bool IsNumber(object? value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Half;

    public static string ToText(object? value) => Unwrap(value) switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        IDictionary dictionary => JsonSerializer.Serialize(dictionary, CompactJson),
        IEnumerable sequence => string.Join(",", sequence.Cast<object?>().Select(ToText)),
        var other => other.ToString() ?? string.Empty,
    };

    public static bool AreEqual(object? left, object? right)
    {
        left = Unwrap(left);
        right = Unwrap(right);

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return CompareNumbers(left, right) == 0;
        }

        return (left, right) switch
        {
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (Enum a, string b) => string.Equals(a.ToString(), b, StringComparison.OrdinalIgnoreCase),
            (string a, Enum b) => string.Equals(a, b.ToString(), StringComparison.OrdinalIgnoreCase),
            _ => left.Equals(right),
        };
    }

    public static int? Compare(object? left, object? right)
    {
        left = Unwrap(left);
        right = Unwrap(right);

        if (left is null || right is null)
        {
            return null;
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return CompareNumbers(left, right);
        }

        // A number and a numeric string compare numerically, as in JavaScript.
        if (IsNumber(left) && right is string rightText && TryParseNumber(rightText, out var rightNumber))
        {
            return ToDouble(left).CompareTo(rightNumber);
        }

        if (left is string leftText && IsNumber(right) && TryParseNumber(leftText, out var leftNumber))
        {
            return leftNumber.CompareTo(ToDouble(right));
        }

        if (left is string a && right is string b)
        {
            return string.CompareOrdinal(a, b);
        }

        if (TryGetMoment(left, out var leftMoment) && TryGetMoment(right, out var rightMoment))
        {
            return leftMoment.CompareTo(rightMoment);
        }

        if (left is IComparable comparable && left.GetType() == right.GetType())
        {
            return comparable.CompareTo(right);
        }

        return null;
    }

    public static bool TryGetInteger(object? value, out int result)
    {
        value = Unwrap(value);
        if (IsNumber(value))
        {
            var number = ToDouble(value!);
            if (!double.IsNaN(number))
            {
                result = (int)Math.Clamp(Math.Truncate(number), int.MinValue, int.MaxValue);
                return true;
            }
        }
        else if (value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = 0;
        return false;
    }

    public static bool TryGetMoment(object? value, out DateTimeOffset moment)
    {
        switch (Unwrap(value))
        {
            case DateTimeOffset dateTimeOffset:
                moment = dateTimeOffset;
                return true;
            case DateTime dateTime:
                moment = dateTime.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
                    : new DateTimeOffset(dateTime);
                return true;
            case DateOnly date:
                moment = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            case string text when LooksLikeDate(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed):
                moment = parsed;
                return true;
            default:
                moment = default;
                return false;
        }
    }

    // Tells date values such as 2026-10-22 apart from format strings such as "yyyy-MM-dd".
    private static bool LooksLikeDate(string text) => text.Length >= 8 && char.IsAsciiDigit(text[0]);

    private static bool TryParseNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static int CompareNumbers(object left, object right)
    {
        if (left is double or float or Half || right is double or float or Half)
        {
            return ToDouble(left).CompareTo(ToDouble(right));
        }

        return ToDecimal(left).CompareTo(ToDecimal(right));
    }

    private static double ToDouble(object value) => value is Half half
        ? (double)half
        : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static decimal ToDecimal(object value) => System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
