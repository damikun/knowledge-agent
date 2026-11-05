using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeAgent.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KnowledgeAgent.Features.People;

public static class AttributeTypes
{
    public const string String = "string";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";

    public static readonly IReadOnlyList<string> All = [String, Number, Boolean, Date];
}

// Trust order: browser < verified < server = admin. A less trusted source never overwrites or clears a value.
// The verified hash signs only the user id, so the visitor can still change those values in their own browser.
public static class AttributeSources
{
    public const string Browser = "browser";
    public const string Verified = "verified";
    public const string Server = "server";
    public const string Admin = "admin";

    public static int Rank(string? source) => source switch
    {
        Verified => 1,
        Server or Admin => 2,
        _ => 0,
    };
}

// Which sources may write a key: any, verified (verified, server and admin) or server (server and admin only).
public static class AttributeTrust
{
    public const string Any = "any";
    public const string Verified = "verified";
    public const string Server = "server";

    public static readonly IReadOnlyList<string> All = [Any, Verified, Server];

    public static bool Allows(string trust, string source) => trust switch
    {
        Verified => AttributeSources.Rank(source) >= 1,
        Server => AttributeSources.Rank(source) >= 2,
        _ => true,
    };
}

public sealed record AttributeStamp(string Source, DateTimeOffset UpdatedAt);

public sealed class ContactAttributeDefinition
{
    public const int MaxLabelLength = 60;

    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = AttributeTypes.String;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    public bool Visible { get; set; } = true;

    public bool ExposeToAgent { get; set; }

    public string Trust { get; set; } = AttributeTrust.Any;

    public static string LabelOf(string key)
    {
        var words = key.Replace('_', ' ').Trim();
        return words.Length == 0 ? key : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

internal sealed class ContactAttributeDefinitionConfiguration : IEntityTypeConfiguration<ContactAttributeDefinition>
{
    public void Configure(EntityTypeBuilder<ContactAttributeDefinition> builder)
    {
        builder.ToTable("contact_attributes");
        builder.HasKey(definition => definition.Key);
        builder.Property(definition => definition.Key).HasMaxLength(ContactAttributeLimits.MaxKeyLength);
        builder.Property(definition => definition.Label).HasMaxLength(ContactAttributeDefinition.MaxLabelLength).IsRequired();
        builder.Property(definition => definition.Type).HasMaxLength(10).IsRequired();
        builder.Property(definition => definition.Trust).HasMaxLength(10).IsRequired();
    }
}

public static partial class ContactAttributeLimits
{
    public const int MaxKeyLength = 40;
    public const int MaxKeysPerContact = 50;
    public const int MaxStringLength = 255;

    // A workspace-wide cap, so a page that sends user ids as keys cannot grow the registry without end.
    public const int MaxRegistryKeys = 200;

    // At most this many keys are looked at per request; the rest are refused.
    public const int MaxKeysPerRequest = 100;

    public static readonly IReadOnlySet<string> ReservedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "email", "user_id", "identified", "first_seen", "last_seen", "sessions", "conversations", "returning",
        "last_page", "referrer", "language", "widget", "country", "notes", "created_at", "updated_at",
    };

    public static bool IsKey(string key) => KeyPattern().IsMatch(key);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

public sealed record AttributeRejection(string Key, string Reason);

public sealed record AttributeChangeResult(IReadOnlyList<string> Applied, IReadOnlyList<AttributeRejection> Rejected)
{
    public static readonly AttributeChangeResult None = new([], []);
}

internal static partial class AttributeValues
{
    // 2026-11-05, 2026-11-05T10:00, 2026-11-05T10:00:00Z, 2026-11-05T10:00:00.123+01:00
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    // Fixed width in UTC, so stored dates sort as text the same way as in time (the Postgres filters rely on it).
    public static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? text, out DateTimeOffset value)
    {
        value = default;
        return text is not null && IsoDate().IsMatch(text.Trim())
            && DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    public static string? InferType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => AttributeTypes.Number,
        JsonValueKind.True or JsonValueKind.False => AttributeTypes.Boolean,
        JsonValueKind.String when TryParseDate(value.GetString(), out _) => AttributeTypes.Date,
        JsonValueKind.String => AttributeTypes.String,
        _ => null,
    };

    public static bool TryConvert(JsonElement value, string type, out JsonElement stored, out string? error)
    {
        stored = default;
        error = null;
        switch (type)
        {
            case AttributeTypes.Number:
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                    || value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    stored = JsonSerializer.SerializeToElement(number);
                    return true;
                }

                error = "Expected a number.";
                return false;
            case AttributeTypes.Boolean:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    stored = JsonSerializer.SerializeToElement(value.GetBoolean());
                    return true;
                }

                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var flag))
                {
                    stored = JsonSerializer.SerializeToElement(flag);
                    return true;
                }

                error = "Expected true or false.";
                return false;
            case AttributeTypes.Date:
                if (value.ValueKind == JsonValueKind.String && TryParseDate(value.GetString(), out var date))
                {
                    stored = JsonSerializer.SerializeToElement(FormatDate(date));
                    return true;
                }

                error = "Expected a date in ISO 8601, such as 2026-11-05 or 2026-11-05T10:00:00Z.";
                return false;
            default:
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString()!.Trim(),
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
                if (text is null)
                {
                    error = "Values must be text, numbers, true or false, or dates.";
                    return false;
                }

                if (text.Length > ContactAttributeLimits.MaxStringLength)
                {
                    error = $"Text values can be at most {ContactAttributeLimits.MaxStringLength} characters long.";
                    return false;
                }

                stored = JsonSerializer.SerializeToElement(StoreText.RemoveNul(text));
                return true;
        }
    }

    // Sent keys overwrite, missing keys keep their value, null clears. New keys go to newDefinitions.
    // Bad input is reported per key, never thrown.
    public static AttributeChangeResult Merge(
        Contact contact,
        IReadOnlyDictionary<string, JsonElement> incoming,
        string source,
        DateTimeOffset now,
        IDictionary<string, ContactAttributeDefinition> registry,
        ICollection<ContactAttributeDefinition> newDefinitions)
    {
        var applied = new List<string>();
        var rejected = new List<AttributeRejection>();
        if (incoming.Count == 0)
        {
            return AttributeChangeResult.None;
        }

        var values = new Dictionary<string, JsonElement>(contact.Attributes, StringComparer.Ordinal);
        var stamps = new Dictionary<string, AttributeStamp>(contact.AttributeSources, StringComparer.Ordinal);
        var seen = 0;
        foreach (var (rawKey, value) in incoming)
        {
            var key = rawKey ?? string.Empty;
            if (++seen > ContactAttributeLimits.MaxKeysPerRequest)
            {
                rejected.Add(new(key, $"At most {ContactAttributeLimits.MaxKeysPerRequest} attributes are read per request."));
                continue;
            }

            if (!ContactAttributeLimits.IsKey(key))
            {
                rejected.Add(new(key, "Keys are 1 to 40 lowercase letters, digits or underscores, starting with a letter."));
                continue;
            }

            if (ContactAttributeLimits.ReservedKeys.Contains(key))
            {
                rejected.Add(new(key, "This name belongs to a built-in attribute."));
                continue;
            }

            registry.TryGetValue(key, out var definition);
            if (definition is not null && !AttributeTrust.Allows(definition.Trust, source))
            {
                rejected.Add(new(key, $"Only {(definition.Trust == AttributeTrust.Server ? "your server" : "verified users or your server")} may set this attribute."));
                continue;
            }

            var current = stamps.GetValueOrDefault(key);
            if (values.ContainsKey(key) && current is not null && AttributeSources.Rank(current.Source) > AttributeSources.Rank(source))
            {
                rejected.Add(new(key, $"Kept: the value was set by a more trusted source ({current.Source})."));
                continue;
            }

            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (values.Remove(key))
                {
                    stamps.Remove(key);
                    applied.Add(key);
                }

                continue;
            }

            var type = definition?.Type ?? InferType(value);
            if (type is null)
            {
                rejected.Add(new(key, "Values must be text, numbers, true or false, or dates."));
                continue;
            }

            if (!TryConvert(value, type, out var stored, out var error))
            {
                rejected.Add(new(key, error!));
                continue;
            }

            if (!values.ContainsKey(key) && values.Count >= ContactAttributeLimits.MaxKeysPerContact)
            {
                rejected.Add(new(key, $"A contact has at most {ContactAttributeLimits.MaxKeysPerContact} attributes."));
                continue;
            }

            if (definition is null)
            {
                if (registry.Count >= ContactAttributeLimits.MaxRegistryKeys)
                {
                    rejected.Add(new(key, $"The workspace has {ContactAttributeLimits.MaxRegistryKeys} attributes already; delete unused ones under Contacts, Attributes."));
                    continue;
                }

                definition = new ContactAttributeDefinition
                {
                    Key = key,
                    Label = ContactAttributeDefinition.LabelOf(key),
                    Type = type,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                };
                registry[key] = definition;
                newDefinitions.Add(definition);
            }
            else if (now - definition.LastSeenAt > TimeSpan.FromHours(1))
            {
                // Hourly at most, so a busy widget does not write the registry on every session.
                definition.LastSeenAt = now;
            }

            // The same value again only raises its source; the time says when it last changed.
            var unchanged = values.TryGetValue(key, out var old) && old.GetRawText() == stored.GetRawText();
            if (!unchanged || current is null || AttributeSources.Rank(source) > AttributeSources.Rank(current.Source))
            {
                values[key] = stored;
                stamps[key] = new AttributeStamp(source, unchanged && current is not null ? current.UpdatedAt : now);
            }

            applied.Add(key);
        }

        contact.Attributes = values;
        contact.AttributeSources = stamps;
        return new AttributeChangeResult(applied, rejected);
    }
}

internal static class ContactJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<Dictionary<string, JsonElement>, string> Values = new(
        value => StoreText.RemoveNulFromJson(JsonSerializer.Serialize(value, Options)),
        json => Read<Dictionary<string, JsonElement>>(json));

    public static readonly ValueComparer<Dictionary<string, JsonElement>> ValuesComparer = new(
        (left, right) => Text(left) == Text(right),
        value => Text(value).GetHashCode(StringComparison.Ordinal),
        value => new Dictionary<string, JsonElement>(value, StringComparer.Ordinal));

    public static readonly ValueConverter<Dictionary<string, AttributeStamp>, string> Stamps = new(
        value => JsonSerializer.Serialize(value, Options),
        json => Read<Dictionary<string, AttributeStamp>>(json));

    public static readonly ValueComparer<Dictionary<string, AttributeStamp>> StampsComparer = new(
        (left, right) => Text(left) == Text(right),
        value => Text(value).GetHashCode(StringComparison.Ordinal),
        value => new Dictionary<string, AttributeStamp>(value, StringComparer.Ordinal));

    private static TDictionary Read<TDictionary>(string json)
        where TDictionary : new() =>
        string.IsNullOrWhiteSpace(json) ? new TDictionary() : JsonSerializer.Deserialize<TDictionary>(json, Options) ?? new TDictionary();

    // Sorted by key, so two dictionaries with the same entries in another order compare equal.
    private static string Text<T>(Dictionary<string, T>? value) =>
        value is null ? string.Empty : JsonSerializer.Serialize(value.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList(), Options);
}
