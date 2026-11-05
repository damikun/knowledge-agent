using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using HandlebarsDotNet;

namespace KnowledgeAgent.Features.Prompts.Rendering;

internal sealed class TemplateHelpers(TimeProvider timeProvider)
{
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTruncateSuffix = "…";

    public static readonly FrozenSet<string> CustomHelperNames = FrozenSet.ToFrozenSet(
        ["eq", "ne", "gt", "gte", "lt", "lte", "and", "or", "not", "default", "json", "upper", "lower", "truncate", "join", "date"],
        StringComparer.Ordinal);

    public static readonly FrozenSet<string> AllHelperNames = FrozenSet.ToFrozenSet(
        CustomHelperNames.Concat(["if", "unless", "each", "with", "lookup", "log"]),
        StringComparer.Ordinal);

    // All are return helpers, so they also work in subexpressions such as {{#if (eq tone "formal")}}.
    public void Register(IHandlebars handlebars)
    {
        handlebars.RegisterHelper("eq", (context, arguments) => Binary(arguments, "eq", TemplateValues.AreEqual));
        handlebars.RegisterHelper("ne", (context, arguments) => Binary(arguments, "ne", (a, b) => !TemplateValues.AreEqual(a, b)));
        handlebars.RegisterHelper("gt", (context, arguments) => Binary(arguments, "gt", (a, b) => TemplateValues.Compare(a, b) > 0));
        handlebars.RegisterHelper("gte", (context, arguments) => Binary(arguments, "gte", (a, b) => TemplateValues.Compare(a, b) >= 0));
        handlebars.RegisterHelper("lt", (context, arguments) => Binary(arguments, "lt", (a, b) => TemplateValues.Compare(a, b) < 0));
        handlebars.RegisterHelper("lte", (context, arguments) => Binary(arguments, "lte", (a, b) => TemplateValues.Compare(a, b) <= 0));
        handlebars.RegisterHelper("and", (context, arguments) => And(arguments));
        handlebars.RegisterHelper("or", (context, arguments) => Or(arguments));
        handlebars.RegisterHelper("not", (context, arguments) => Not(arguments));
        handlebars.RegisterHelper("default", (context, arguments) => Default(arguments));
        handlebars.RegisterHelper("json", (context, arguments) => Json(arguments));
        handlebars.RegisterHelper("upper", (context, arguments) => Upper(arguments));
        handlebars.RegisterHelper("lower", (context, arguments) => Lower(arguments));
        handlebars.RegisterHelper("truncate", (context, arguments) => Truncate(arguments));
        handlebars.RegisterHelper("join", (context, arguments) => Join(arguments));
        handlebars.RegisterHelper("date", (context, arguments) => Date(arguments));
    }

    private static object Binary(Arguments arguments, string name, Func<object?, object?, bool> predicate)
    {
        ExpectArguments(arguments, name, 2, 2);
        return predicate(arguments[0], arguments[1]);
    }

    private static object And(Arguments arguments)
    {
        ExpectArguments(arguments, "and", 1, int.MaxValue);
        for (var i = 0; i < arguments.Length; i++)
        {
            if (!TemplateValues.IsTruthy(arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static object Or(Arguments arguments)
    {
        ExpectArguments(arguments, "or", 1, int.MaxValue);
        for (var i = 0; i < arguments.Length; i++)
        {
            if (TemplateValues.IsTruthy(arguments[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static object Not(Arguments arguments)
    {
        ExpectArguments(arguments, "not", 1, 1);
        return !TemplateValues.IsTruthy(arguments[0]);
    }

    private static object Default(Arguments arguments)
    {
        ExpectArguments(arguments, "default", 2, int.MaxValue);
        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (!TemplateValues.IsMissing(arguments[i]))
            {
                return TemplateValues.Unwrap(arguments[i])!;
            }
        }

        return TemplateValues.Unwrap(arguments[arguments.Length - 1]) ?? string.Empty;
    }

    private static object Json(Arguments arguments)
    {
        ExpectArguments(arguments, "json", 1, 2);
        var indented = arguments.Length == 2 && TemplateValues.IsTruthy(arguments[1]);
        return JsonSerializer.Serialize(TemplateValues.Unwrap(arguments[0]), indented ? TemplateValues.IndentedJson : TemplateValues.CompactJson);
    }

    private static object Upper(Arguments arguments)
    {
        ExpectArguments(arguments, "upper", 1, 1);
        return TemplateValues.ToText(arguments[0]).ToUpperInvariant();
    }

    private static object Lower(Arguments arguments)
    {
        ExpectArguments(arguments, "lower", 1, 1);
        return TemplateValues.ToText(arguments[0]).ToLowerInvariant();
    }

    private static object Truncate(Arguments arguments)
    {
        ExpectArguments(arguments, "truncate", 2, 3);
        var text = TemplateValues.ToText(arguments[0]);
        if (!TemplateValues.TryGetInteger(arguments[1], out var length))
        {
            throw HelperError("truncate", "the second argument (maximum length) must be a number");
        }

        length = Math.Max(0, length);
        if (text.Length <= length)
        {
            return text;
        }

        var cut = length;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        var suffix = arguments.Length == 3 ? TemplateValues.ToText(arguments[2]) : DefaultTruncateSuffix;
        return string.Concat(text.AsSpan(0, cut).TrimEnd(), suffix);
    }

    private static object Join(Arguments arguments)
    {
        ExpectArguments(arguments, "join", 1, 2);
        var separator = arguments.Length == 2 ? TemplateValues.ToText(arguments[1]) : ", ";
        return TemplateValues.Unwrap(arguments[0]) switch
        {
            null => string.Empty,
            string text => text,
            IDictionary dictionary => JoinItems(dictionary.Values, separator),
            IEnumerable sequence => JoinItems(sequence, separator),
            var other => TemplateValues.ToText(other),
        };

        static string JoinItems(IEnumerable items, string separator) => string.Join(
            separator,
            items.Cast<object?>().Select(TemplateValues.Unwrap).Where(item => item is not null).Select(TemplateValues.ToText));
    }

    private object Date(Arguments arguments)
    {
        ExpectArguments(arguments, "date", 0, 2);

        DateTimeOffset? moment;
        string? format = null;
        switch (arguments.Length)
        {
            case 0:
                moment = timeProvider.GetUtcNow();
                break;
            case 1:
            {
                var value = TemplateValues.Unwrap(arguments[0]);
                if (TryResolveMoment(value, out moment))
                {
                    break;
                }

                // A lone string that is not a date is a format for the current time, as in {{date "yyyy-MM-dd"}}.
                if (value is string text)
                {
                    moment = timeProvider.GetUtcNow();
                    format = text;
                }

                break;
            }
            default:
            {
                var first = TemplateValues.Unwrap(arguments[0]);
                var second = TemplateValues.Unwrap(arguments[1]);
                if (TryResolveMoment(first, out moment))
                {
                    format = TemplateValues.ToText(second);
                }
                else if (TryResolveMoment(second, out moment))
                {
                    format = TemplateValues.ToText(first);
                }

                break;
            }
        }

        if (moment is null)
        {
            return string.Empty;
        }

        try
        {
            return moment.Value.ToString(string.IsNullOrEmpty(format) ? DefaultDateFormat : format, CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new PromptRenderException($"Helper 'date': '{format}' is not a valid date format.", ex);
        }
    }

    private bool TryResolveMoment(object? value, out DateTimeOffset? moment)
    {
        if (value is string text && string.Equals(text.Trim(), "now", StringComparison.OrdinalIgnoreCase))
        {
            moment = timeProvider.GetUtcNow();
            return true;
        }

        if (TemplateValues.TryGetMoment(value, out var parsed))
        {
            moment = parsed;
            return true;
        }

        moment = null;
        return false;
    }

    private static void ExpectArguments(Arguments arguments, string name, int min, int max)
    {
        if (arguments.Length < min || arguments.Length > max)
        {
            var expected = min == max ? $"{min}" : max == int.MaxValue ? $"at least {min}" : $"{min} to {max}";
            throw HelperError(name, $"expects {expected} argument(s) but got {arguments.Length}");
        }
    }

    private static PromptRenderException HelperError(string name, string problem) => new($"Helper '{name}' {problem}.");
}
