using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Widgets;

// UserHash is hex HMAC-SHA256(identity secret, UserId); Name, Email and Attributes are not signed.
public sealed record VisitorUserRequest(
    string? UserId,
    string? UserHash,
    string? Name = null,
    string? Email = null,
    Dictionary<string, JsonElement>? Attributes = null);

public sealed record VerifiedUser(string UserId, string? Name, string? Email, IReadOnlyDictionary<string, JsonElement> Attributes);

public sealed class IdentityRequiredException() : Exception("This chat is only for signed-in users.");

internal sealed partial class WidgetIdentity(IDataProtectionProvider dataProtection, ILogger<WidgetIdentity> logger)
{
    // Changing the purpose makes every saved secret undecryptable.
    private readonly IDataProtector _protector = dataProtection.CreateProtector("KnowledgeAgent.Widgets.IdentitySecret.v1");

    public static string NewSecret() => "ka_ivs_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public string Protect(string secret) => _protector.Protect(secret);

    public static string Hash(string secret, string userId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(userId)));

    // Null for no secret, a wrong hash or out-of-bounds fields: the user is then anonymous, or refused if verification is required.
    public VerifiedUser? Verify(Widget widget, VisitorUserRequest? user)
    {
        if (user is null || widget.ProtectedIdentitySecret is null)
        {
            return null;
        }

        var userId = user.UserId?.Trim();
        var hash = user.UserHash?.Trim();
        if (string.IsNullOrEmpty(userId) || userId.Length > People.Contact.MaxExternalIdLength || hash is not { Length: 64 } || !IsHex(hash))
        {
            LogRejected(logger, widget.Id, "malformed");
            return null;
        }

        string secret;
        try
        {
            secret = _protector.Unprotect(widget.ProtectedIdentitySecret);
        }
        catch (CryptographicException ex)
        {
            LogSecretUnreadable(logger, ex, widget.Id);
            return null;
        }

        // Constant time, so the response time tells nothing about how much of the hash was right.
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(userId));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash)))
        {
            LogRejected(logger, widget.Id, "hash mismatch");
            return null;
        }

        var name = user.Name?.Trim();
        var email = user.Email?.Trim();
        return new VerifiedUser(
            userId,
            name is { Length: > 0 and <= People.Contact.MaxNameLength } ? name : null,
            email is { Length: > 0 } && WidgetEmails.IsValid(email) ? email : null,
            Attributes(user.Attributes));
    }

    public static IReadOnlyDictionary<string, JsonElement> Attributes(Dictionary<string, JsonElement>? attributes) =>
        (attributes ?? []).Take(People.ContactAttributeLimits.MaxKeysPerRequest + 1).ToDictionary(StringComparer.Ordinal);

    // Server-to-server requests: X-KA-Signature is hex(HMAC-SHA256(identity secret, "{timestamp}.{userId}.{body}"))
    // with X-KA-Timestamp in Unix seconds, at most five minutes off, so a captured request cannot be replayed later.
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, long timestamp, string userId, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp}.{userId}.");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message));
    }

    public bool VerifyServerRequest(Widget widget, string userId, string? timestamp, string? signature, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (widget.ProtectedIdentitySecret is null
            || !long.TryParse(timestamp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || signature?.Trim() is not { Length: 64 } hash || !IsHex(hash))
        {
            LogRejected(logger, widget.Id, "server request without a valid timestamp or signature");
            return false;
        }

        if ((now - DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799))).Duration() > MaxClockSkew)
        {
            LogRejected(logger, widget.Id, "server request timestamp too far off");
            return false;
        }

        string secret;
        try
        {
            secret = _protector.Unprotect(widget.ProtectedIdentitySecret);
        }
        catch (CryptographicException ex)
        {
            LogSecretUnreadable(logger, ex, widget.Id);
            return false;
        }

        var expected = Convert.FromHexString(Sign(secret, seconds, userId, body));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash)))
        {
            LogRejected(logger, widget.Id, "server signature mismatch");
            return false;
        }

        return true;
    }

    private static bool IsHex(string value) => value.All(char.IsAsciiHexDigit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignored the user identity sent to widget {WidgetId}: {Reason}")]
    private static partial void LogRejected(ILogger logger, Guid widgetId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The identity secret of widget {WidgetId} cannot be decrypted (the Data Protection key ring changed?); create a new one in the widget's settings")]
    private static partial void LogSecretUnreadable(ILogger logger, Exception exception, Guid widgetId);
}
