using System.Net.Mail;

namespace KnowledgeAgent.Features.Widgets;

internal static class ImageTypes
{
    public static string? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}

internal static class WidgetOrigins
{
    // scheme://host[:port], lower case, no path; the host may start with *. for every subdomain.
    public static string? Normalize(string? origin)
    {
        var trimmed = origin?.Trim().TrimEnd('/') ?? string.Empty;
        var wildcard = false;
        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0 && trimmed.AsSpan(schemeEnd + 3).StartsWith("*."))
        {
            wildcard = true;
            trimmed = trimmed.Remove(schemeEnd + 3, 2);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || trimmed.Length > 200)
        {
            return null;
        }

        var host = (wildcard ? "*." : string.Empty) + uri.IdnHost.ToLowerInvariant();
        return uri.IsDefaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{uri.Port}";
    }
}

internal static class WidgetEmails
{
    public static bool IsValid(string? email) =>
        email is { Length: > 2 and <= WidgetVisitor.MaxEmailLength }
        && MailAddress.TryCreate(email, out var address)
        && address.Address == email
        && address.Host.Contains('.', StringComparison.Ordinal);
}

internal static class WidgetSchedule
{
    public static bool IsKnownTimeZone(string? id) => id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static bool IsOpen(WidgetAvailability availability, DateTimeOffset now)
    {
        if (availability.Always)
        {
            return true;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(availability.TimeZone, out var zone))
        {
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        return availability.Hours.Any(hours => hours.Day == local.DayOfWeek && time >= hours.Opens && time < hours.Closes);
    }
}
