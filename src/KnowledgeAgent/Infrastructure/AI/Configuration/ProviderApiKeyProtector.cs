using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Infrastructure.AI.Configuration;

internal sealed partial class ProviderApiKeyProtector(IDataProtectionProvider dataProtection, ILogger<ProviderApiKeyProtector> logger)
{
    // Changing the purpose makes every saved key undecryptable.
    internal const string Purpose = "KnowledgeAgent.AI.ProviderApiKeys.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);
    private readonly ConcurrentDictionary<string, byte> _reportedFailures = new(StringComparer.OrdinalIgnoreCase);

    public string Protect(string apiKey) => _protector.Protect(apiKey);

    public string? TryUnprotect(string protectedValue, string providerKey)
    {
        try
        {
            var value = _protector.Unprotect(protectedValue);
            _reportedFailures.TryRemove(providerKey, out _);
            return value;
        }
        catch (CryptographicException ex)
        {
            if (_reportedFailures.TryAdd(providerKey, 0))
            {
                LogUnprotectFailed(logger, ex, providerKey);
            }

            return null;
        }
    }

    // Short keys get no suffix so the hint never reveals a large part of them.
    public static string Hint(string apiKey) => apiKey.Length >= 12 ? "…" + apiKey[^4..] : "…";

    [LoggerMessage(Level = LogLevel.Warning, Message = "The API key saved for provider '{ProviderKey}' cannot be decrypted (the Data Protection key ring changed?); save it again under Models → Providers")]
    private static partial void LogUnprotectFailed(ILogger logger, Exception exception, string providerKey);
}
