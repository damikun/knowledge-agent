using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Widgets;

public sealed record VisitorIdentity(Guid WidgetId, Guid VisitorId, bool Verified = false, bool Preview = false);

// Visitor tokens are encrypted and signed with the Data Protection key ring that the instances share, so any instance
// can read them and nobody can forge one for another visitor or widget.
internal sealed class VisitorTokens(IDataProtectionProvider dataProtection, IOptions<WidgetOptions> options, TimeProvider timeProvider)
{
    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("KnowledgeAgent.Widgets.Visitor.v1").ToTimeLimitedDataProtector();

    private const string VerifiedFlag = "v";
    private const string PreviewFlag = "p";

    public (string Token, DateTimeOffset ExpiresAt) Issue(VisitorIdentity identity)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = identity.Preview
            ? now.AddMinutes(Math.Max(1, options.Value.PreviewTokenLifetimeMinutes))
            : now.AddDays(Math.Max(1, options.Value.VisitorTokenLifetimeDays));
        var payload = $"{identity.WidgetId:N}.{identity.VisitorId:N}"
            + (identity.Verified ? "." + VerifiedFlag : string.Empty)
            + (identity.Preview ? "." + PreviewFlag : string.Empty);
        return (_protector.Protect(payload, expiresAt), expiresAt);
    }

    public VisitorIdentity? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000)
        {
            return null;
        }

        try
        {
            var payload = _protector.Unprotect(token.Trim(), out var expiration);
            if (expiration <= timeProvider.GetUtcNow())
            {
                return null;
            }

            // Tokens issued before identity verification have two parts; the flags follow in a fixed order.
            var parts = payload.Split('.');
            var flags = parts.Skip(2).ToArray();
            var verified = flags.FirstOrDefault() == VerifiedFlag;
            var preview = flags.LastOrDefault() == PreviewFlag;
            var known = flags.Length == (verified ? 1 : 0) + (preview ? 1 : 0);
            return parts.Length is >= 2 and <= 4
                   && known
                   && Guid.TryParseExact(parts[0], "N", out var widgetId)
                   && Guid.TryParseExact(parts[1], "N", out var visitorId)
                ? new VisitorIdentity(widgetId, visitorId, verified, preview)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

// Sliding one-minute windows per key, per instance: enough to stop a single client from flooding the agent.
internal sealed class WidgetRateLimiter(TimeProvider timeProvider)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int CleanupThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _windows = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, int limit, out TimeSpan retryAfter)
    {
        var now = timeProvider.GetUtcNow();
        if (_windows.Count > CleanupThreshold)
        {
            Cleanup(now);
        }

        while (true)
        {
            var window = _windows.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            lock (window)
            {
                // A cleanup removed this window between the lookup and the lock: count in the one that replaces it.
                if (!_windows.TryGetValue(key, out var current) || !ReferenceEquals(current, window))
                {
                    continue;
                }

                while (window.Count > 0 && now - window.Peek() >= Window)
                {
                    window.Dequeue();
                }

                if (window.Count >= limit)
                {
                    retryAfter = Window - (now - window.Peek());
                    return false;
                }

                window.Enqueue(now);
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }

    public static string RetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var (key, window) in _windows)
        {
            lock (window)
            {
                if (window.Count == 0 || now - window.Peek() >= Window)
                {
                    _windows.TryRemove(new KeyValuePair<string, Queue<DateTimeOffset>>(key, window));
                }
            }
        }
    }
}
