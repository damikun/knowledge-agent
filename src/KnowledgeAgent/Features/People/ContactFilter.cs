using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeAgent.Features.People;

public sealed record ContactCondition(string Attr, string Op, JsonElement? Value = null);

public static class ContactFilterOps
{
    public const string Is = "is";
    public const string IsNot = "is_not";
    public const string Contains = "contains";
    public const string Set = "set";
    public const string NotSet = "not_set";
    public const string Eq = "eq";
    public const string Neq = "neq";
    public const string Gt = "gt";
    public const string Lt = "lt";
    public const string Between = "between";
    public const string True = "true";
    public const string False = "false";
    public const string Before = "before";
    public const string After = "after";
    public const string WithinDays = "within_days";

    public const string WidgetType = "widget";

    public static IReadOnlyList<string> For(string type, bool builtIn) => type switch
    {
        AttributeTypes.Number => builtIn ? [Eq, Neq, Gt, Lt, Between] : [Eq, Neq, Gt, Lt, Between, Set, NotSet],
        AttributeTypes.Boolean => builtIn ? [True, False] : [True, False, Set, NotSet],
        AttributeTypes.Date => builtIn ? [Before, After, WithinDays] : [Before, After, WithinDays, Set, NotSet],
        WidgetType => [Is, IsNot],
        _ => [Is, IsNot, Contains, Set, NotSet],
    };
}

public sealed record ContactFilterAttributeDto(string Key, string Label, string Type, bool BuiltIn, IReadOnlyList<string> Ops);

public static class ContactFilterAttributes
{
    public static readonly IReadOnlyList<ContactFilterAttributeDto> All =
    [
        Built("name", "Name", AttributeTypes.String),
        Built("email", "Email", AttributeTypes.String),
        Built("user_id", "User id", AttributeTypes.String),
        Built("identified", "Identified", AttributeTypes.Boolean),
        Built("returning", "Returning", AttributeTypes.Boolean),
        Built("first_seen", "First seen", AttributeTypes.Date),
        Built("last_seen", "Last seen", AttributeTypes.Date),
        Built("sessions", "Sessions", AttributeTypes.Number),
        Built("conversations", "Conversations", AttributeTypes.Number),
        Built("last_page", "Last page", AttributeTypes.String),
        Built("referrer", "Referrer", AttributeTypes.String),
        Built("language", "Browser language", AttributeTypes.String),
        Built("widget", "Widget used", ContactFilterOps.WidgetType),
    ];

    public static readonly IReadOnlyDictionary<string, ContactFilterAttributeDto> ByKey = All.ToDictionary(item => item.Key, StringComparer.Ordinal);

    private static ContactFilterAttributeDto Built(string key, string label, string type) => new(key, label, type, true, ContactFilterOps.For(type, builtIn: true));
}

public sealed record ResolvedCondition(string Attr, bool BuiltIn, string Type, string Op)
{
    public string? Text { get; init; }
    public decimal? Number { get; init; }
    public decimal? Number2 { get; init; }
    public DateTimeOffset? Date { get; init; }
    public int? Days { get; init; }
    public Guid? WidgetId { get; init; }
}

public static class ContactFilters
{
    public const int MaxConditions = 20;
    public const int MaxDays = 3650;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(IReadOnlyList<ContactCondition> conditions) => JsonSerializer.Serialize(conditions, Options);

    public static IReadOnlyList<ContactCondition> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<ContactCondition>>(json, Options) ?? [];

    public static string Encode(IReadOnlyList<ContactCondition> conditions) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Serialize(conditions))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // filter= is base64url JSON of the conditions ([{"attr":"plan","op":"is","value":"pro"}]); f= repeats attr:op:value
    // (between as from..to, within_days as a number), handy by hand: f=plan:is:pro&f=seats:between:5..10.
    public static bool TryParse(string? filter, IReadOnlyList<string>? f, out List<ContactCondition> conditions, out string? error)
    {
        conditions = [];
        error = null;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            try
            {
                var base64 = filter.Trim().Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
                conditions.AddRange(Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(base64))));
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                error = "filter must be base64url-encoded JSON: an array of { attr, op, value }.";
                return false;
            }
        }

        foreach (var item in f ?? [])
        {
            var parts = item.Split(':', 3);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                error = "f must be attr:op or attr:op:value, such as plan:is:pro.";
                return false;
            }

            JsonElement? value = null;
            if (parts.Length == 3)
            {
                var text = parts[2];
                var range = text.Split("..", 2);
                value = parts[1] == ContactFilterOps.Between && range.Length == 2
                    ? JsonSerializer.SerializeToElement(range)
                    : JsonSerializer.SerializeToElement(text);
            }

            conditions.Add(new ContactCondition(parts[0], parts[1], value));
        }

        if (conditions.Count > MaxConditions)
        {
            error = $"A filter has at most {MaxConditions} conditions.";
            return false;
        }

        return true;
    }

    // Strict for request input; lenient for saved segments, whose keys may have been deleted since (they match nobody).
    public static List<ResolvedCondition> Resolve(
        IReadOnlyList<ContactCondition> conditions,
        IReadOnlyDictionary<string, ContactAttributeDefinition> registry,
        bool strict,
        List<string> errors)
    {
        var resolved = new List<ResolvedCondition>();
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var attr = condition.Attr?.Trim() ?? string.Empty;
            var op = condition.Op?.Trim().ToLowerInvariant() ?? string.Empty;
            string type;
            bool builtIn;
            if (ContactFilterAttributes.ByKey.TryGetValue(attr, out var builtInAttribute))
            {
                type = builtInAttribute.Type;
                builtIn = true;
            }
            else if (registry.TryGetValue(attr, out var definition))
            {
                type = definition.Type;
                builtIn = false;
            }
            else if (!strict && ContactAttributeLimits.IsKey(attr))
            {
                type = TypeOfOp(op);
                builtIn = false;
            }
            else
            {
                errors.Add($"Condition {index + 1}: '{attr}' is not an attribute.");
                continue;
            }

            if (!ContactFilterOps.For(type, builtIn).Contains(op))
            {
                errors.Add($"Condition {index + 1}: {attr} ({type}) takes {string.Join(", ", ContactFilterOps.For(type, builtIn))}, not '{op}'.");
                continue;
            }

            var item = new ResolvedCondition(attr, builtIn, type, op);
            var value = condition.Value;
            switch (op)
            {
                case ContactFilterOps.Set or ContactFilterOps.NotSet or ContactFilterOps.True or ContactFilterOps.False:
                    break;
                case ContactFilterOps.Is or ContactFilterOps.IsNot or ContactFilterOps.Contains when type == ContactFilterOps.WidgetType:
                    if (Text(value) is { } widgetText && Guid.TryParse(widgetText, out var widgetId))
                    {
                        item = item with { WidgetId = widgetId };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: widget takes a widget id.");
                        continue;
                    }

                    break;
                case ContactFilterOps.Is or ContactFilterOps.IsNot or ContactFilterOps.Contains:
                    if (Text(value) is { Length: > 0 and <= ContactAttributeLimits.MaxStringLength } text)
                    {
                        item = item with { Text = text };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: {attr} {op} takes a text of 1 to {ContactAttributeLimits.MaxStringLength} characters.");
                        continue;
                    }

                    break;
                case ContactFilterOps.Between:
                    if (value is { ValueKind: JsonValueKind.Array } range && range.GetArrayLength() == 2
                        && Number(range[0]) is { } from && Number(range[1]) is { } to && from <= to)
                    {
                        item = item with { Number = from, Number2 = to };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: between takes [from, to], the first not above the second.");
                        continue;
                    }

                    break;
                case ContactFilterOps.Eq or ContactFilterOps.Neq or ContactFilterOps.Gt or ContactFilterOps.Lt:
                    if (value is { } numberValue && Number(numberValue) is { } number)
                    {
                        item = item with { Number = number };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: {attr} {op} takes a number.");
                        continue;
                    }

                    break;
                case ContactFilterOps.Before or ContactFilterOps.After:
                    if (AttributeValues.TryParseDate(Text(value), out var date))
                    {
                        item = item with { Date = date };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: {attr} {op} takes a date such as 2026-11-05.");
                        continue;
                    }

                    break;
                case ContactFilterOps.WithinDays:
                    if (value is { } daysValue && Number(daysValue) is { } days && days >= 1 && days <= MaxDays && days == decimal.Truncate(days))
                    {
                        item = item with { Days = (int)days };
                    }
                    else
                    {
                        errors.Add($"Condition {index + 1}: within_days takes a whole number of days from 1 to {MaxDays}.");
                        continue;
                    }

                    break;
            }

            resolved.Add(item);
        }

        return resolved;
    }

    private static string TypeOfOp(string op) => op switch
    {
        ContactFilterOps.Eq or ContactFilterOps.Neq or ContactFilterOps.Gt or ContactFilterOps.Lt or ContactFilterOps.Between => AttributeTypes.Number,
        ContactFilterOps.True or ContactFilterOps.False => AttributeTypes.Boolean,
        ContactFilterOps.Before or ContactFilterOps.After or ContactFilterOps.WithinDays => AttributeTypes.Date,
        _ => AttributeTypes.String,
    };

    private static string? Text(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.String } text => text.GetString()?.Trim(),
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        _ => null,
    };

    private static decimal? Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    public static DateTimeOffset Cutoff(ResolvedCondition condition, DateTimeOffset now) => now.AddDays(-(condition.Days ?? 0));

    public static bool Matches(Contact contact, IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now, Func<Guid, IReadOnlySet<Guid>> widgetsOf) =>
        conditions.All(condition => Matches(contact, condition, now, widgetsOf));

    private static bool Matches(Contact contact, ResolvedCondition condition, DateTimeOffset now, Func<Guid, IReadOnlySet<Guid>> widgetsOf)
    {
        if (condition.BuiltIn)
        {
            switch (condition.Attr)
            {
                case "widget":
                    var used = widgetsOf(contact.Id).Contains(condition.WidgetId!.Value);
                    return condition.Op == ContactFilterOps.Is ? used : !used;
                case "identified":
                    return contact.Identified == (condition.Op == ContactFilterOps.True);
                case "returning":
                    return contact.Returning == (condition.Op == ContactFilterOps.True);
                case "sessions":
                    return CompareNumber(contact.SessionCount, condition);
                case "conversations":
                    return CompareNumber(contact.ConversationCount, condition);
                case "first_seen":
                    return CompareDate(contact.FirstSeenAt, condition, now);
                case "last_seen":
                    return CompareDate(contact.LastSeenAt, condition, now);
                default:
                    var text = condition.Attr switch
                    {
                        "name" => contact.Name,
                        "email" => contact.Email,
                        "user_id" => contact.ExternalId,
                        "last_page" => contact.LastPageUrl,
                        "referrer" => contact.LastReferrer,
                        "language" => contact.Language,
                        _ => null,
                    };
                    return condition.Op switch
                    {
                        ContactFilterOps.Is => string.Equals(text, condition.Text, StringComparison.OrdinalIgnoreCase),
                        ContactFilterOps.IsNot => !string.Equals(text, condition.Text, StringComparison.OrdinalIgnoreCase),
                        ContactFilterOps.Contains => text is not null && text.Contains(condition.Text!, StringComparison.OrdinalIgnoreCase),
                        ContactFilterOps.Set => text is not null,
                        _ => text is null,
                    };
            }
        }

        var has = contact.Attributes.TryGetValue(condition.Attr, out var value);
        switch (condition.Op)
        {
            case ContactFilterOps.Set:
                return has;
            case ContactFilterOps.NotSet:
                return !has;
            case ContactFilterOps.True or ContactFilterOps.False:
                return has && value.ValueKind == (condition.Op == ContactFilterOps.True ? JsonValueKind.True : JsonValueKind.False);
            case ContactFilterOps.Is:
                return has && value.ValueKind == JsonValueKind.String && value.GetString() == condition.Text;
            case ContactFilterOps.IsNot:
                return !(has && value.ValueKind == JsonValueKind.String && value.GetString() == condition.Text);
            case ContactFilterOps.Contains:
                return has && value.ValueKind == JsonValueKind.String && value.GetString()!.Contains(condition.Text!, StringComparison.OrdinalIgnoreCase);
            case ContactFilterOps.Neq:
                return !(has && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var other) && other == condition.Number);
            case ContactFilterOps.Eq or ContactFilterOps.Gt or ContactFilterOps.Lt or ContactFilterOps.Between:
                return has && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && CompareNumber(number, condition);
            default:
                return has && value.ValueKind == JsonValueKind.String && AttributeValues.TryParseDate(value.GetString(), out var date) && CompareDate(date, condition, now);
        }
    }

    private static bool CompareNumber(decimal value, ResolvedCondition condition) => condition.Op switch
    {
        ContactFilterOps.Eq => value == condition.Number,
        ContactFilterOps.Neq => value != condition.Number,
        ContactFilterOps.Gt => value > condition.Number,
        ContactFilterOps.Lt => value < condition.Number,
        _ => value >= condition.Number && value <= condition.Number2,
    };

    private static bool CompareDate(DateTimeOffset value, ResolvedCondition condition, DateTimeOffset now) => condition.Op switch
    {
        ContactFilterOps.Before => value < condition.Date,
        ContactFilterOps.After => value > condition.Date,
        _ => value >= Cutoff(condition, now),
    };

    // PostgreSQL. Keys are validated against ^[a-z][a-z0-9_]*$ before they get here and values are parameters.
    // JSON path keeps types apart (a number never equals a text), and the jsonb_path_ops GIN index serves equality.

    public static (string Sql, List<NpgsqlParameter> Parameters) ToSql(IReadOnlyList<ResolvedCondition> conditions, DateTimeOffset now)
    {
        var parameters = new List<NpgsqlParameter>();
        var clauses = conditions.Select(condition => Clause(condition, now, parameters)).ToList();
        return (clauses.Count == 0 ? "TRUE" : string.Join(" AND ", clauses), parameters);
    }

    private static string Clause(ResolvedCondition condition, DateTimeOffset now, List<NpgsqlParameter> parameters)
    {
        string Add(object value, NpgsqlDbType type)
        {
            var name = $"cf{parameters.Count}";
            parameters.Add(new NpgsqlParameter(name, type) { Value = value });
            return "@" + name;
        }

        if (condition.BuiltIn)
        {
            switch (condition.Attr)
            {
                case "widget":
                    var exists = $"EXISTS (SELECT 1 FROM widget_visitors v WHERE v.contact_id = contacts.id AND v.widget_id = {Add(condition.WidgetId!.Value, NpgsqlDbType.Uuid)})";
                    return condition.Op == ContactFilterOps.Is ? exists : $"NOT {exists}";
                case "identified":
                    return condition.Op == ContactFilterOps.True ? "contacts.external_id IS NOT NULL" : "contacts.external_id IS NULL";
                case "returning":
                    const string returning = "(contacts.session_count > 1 OR contacts.conversation_count > 1)";
                    return condition.Op == ContactFilterOps.True ? returning : $"NOT {returning}";
                case "sessions":
                    return NumberClause("contacts.session_count", condition, Add);
                case "conversations":
                    return NumberClause("contacts.conversation_count", condition, Add);
                case "first_seen":
                    return DateClause("contacts.first_seen_at", condition, now, Add);
                case "last_seen":
                    return DateClause("contacts.last_seen_at", condition, now, Add);
                default:
                    var column = condition.Attr switch
                    {
                        "name" => "contacts.name",
                        "email" => "contacts.email",
                        "user_id" => "contacts.external_id",
                        "last_page" => "contacts.last_page_url",
                        "referrer" => "contacts.last_referrer",
                        _ => "contacts.language",
                    };
                    return condition.Op switch
                    {
                        ContactFilterOps.Is => $"lower({column}) = lower({Add(condition.Text!, NpgsqlDbType.Text)})",
                        ContactFilterOps.IsNot => $"({column} IS NULL OR lower({column}) <> lower({Add(condition.Text!, NpgsqlDbType.Text)}))",
                        ContactFilterOps.Contains => $"{column} ILIKE {Add(LikePattern(condition.Text!), NpgsqlDbType.Text)} ESCAPE '\\'",
                        ContactFilterOps.Set => $"{column} IS NOT NULL",
                        _ => $"{column} IS NULL",
                    };
            }
        }

        var path = $"$.\"{condition.Attr}\"";
        string Json(object value) => $"contacts.attributes @> CAST({Add(JsonSerializer.Serialize(new Dictionary<string, object> { [condition.Attr] = value }), NpgsqlDbType.Text)} AS jsonb)";
        string Match(string expression) => $"coalesce(contacts.attributes @@ CAST({Add(expression, NpgsqlDbType.Text)} AS jsonpath), false)";
        string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        string Quoted(string value) => JsonSerializer.Serialize(value);
        return condition.Op switch
        {
            ContactFilterOps.Set => $"contacts.attributes @? CAST({Add(path, NpgsqlDbType.Text)} AS jsonpath)",
            ContactFilterOps.NotSet => $"NOT (contacts.attributes @? CAST({Add(path, NpgsqlDbType.Text)} AS jsonpath))",
            ContactFilterOps.True => Json(true),
            ContactFilterOps.False => Json(false),
            ContactFilterOps.Is => Json(condition.Text!),
            ContactFilterOps.IsNot => $"NOT {Json(condition.Text!)}",
            ContactFilterOps.Contains => $"(jsonb_typeof(contacts.attributes -> '{condition.Attr}') = 'string' AND contacts.attributes ->> '{condition.Attr}' ILIKE {Add(LikePattern(condition.Text!), NpgsqlDbType.Text)} ESCAPE '\\')",
            ContactFilterOps.Eq => Match($"{path} == {Number(condition.Number!.Value)}"),
            ContactFilterOps.Neq => $"NOT {Match($"{path} == {Number(condition.Number!.Value)}")}",
            ContactFilterOps.Gt => Match($"{path} > {Number(condition.Number!.Value)}"),
            ContactFilterOps.Lt => Match($"{path} < {Number(condition.Number!.Value)}"),
            ContactFilterOps.Between => Match($"{path} >= {Number(condition.Number!.Value)} && {path} <= {Number(condition.Number2!.Value)}"),
            ContactFilterOps.Before => Match($"{path} < {Quoted(AttributeValues.FormatDate(condition.Date!.Value))}"),
            ContactFilterOps.After => Match($"{path} > {Quoted(AttributeValues.FormatDate(condition.Date!.Value))}"),
            _ => Match($"{path} >= {Quoted(AttributeValues.FormatDate(Cutoff(condition, now)))}"),
        };
    }

    private static string NumberClause(string column, ResolvedCondition condition, Func<object, NpgsqlDbType, string> add) => condition.Op switch
    {
        ContactFilterOps.Eq => $"{column} = {add(condition.Number!.Value, NpgsqlDbType.Numeric)}",
        ContactFilterOps.Neq => $"{column} <> {add(condition.Number!.Value, NpgsqlDbType.Numeric)}",
        ContactFilterOps.Gt => $"{column} > {add(condition.Number!.Value, NpgsqlDbType.Numeric)}",
        ContactFilterOps.Lt => $"{column} < {add(condition.Number!.Value, NpgsqlDbType.Numeric)}",
        _ => $"{column} BETWEEN {add(condition.Number!.Value, NpgsqlDbType.Numeric)} AND {add(condition.Number2!.Value, NpgsqlDbType.Numeric)}",
    };

    private static string DateClause(string column, ResolvedCondition condition, DateTimeOffset now, Func<object, NpgsqlDbType, string> add) => condition.Op switch
    {
        ContactFilterOps.Before => $"{column} < {add(condition.Date!.Value.ToUniversalTime(), NpgsqlDbType.TimestampTz)}",
        ContactFilterOps.After => $"{column} > {add(condition.Date!.Value.ToUniversalTime(), NpgsqlDbType.TimestampTz)}",
        _ => $"{column} >= {add(Cutoff(condition, now).ToUniversalTime(), NpgsqlDbType.TimestampTz)}",
    };

    private static string LikePattern(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
