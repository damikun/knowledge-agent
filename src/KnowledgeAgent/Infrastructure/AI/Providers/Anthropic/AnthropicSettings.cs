using System.Globalization;
using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Infrastructure.AI.Providers.Anthropic;

internal enum RefusalFallbackMode
{
    None,
    Default,
    Models,
}

internal enum ThinkingMismatchMode
{
    Off,
    Error,
    DropBlock,
}

internal sealed record AnthropicSettings(
    RefusalFallbackMode RefusalFallback,
    IReadOnlyList<string> FallbackModels,
    bool PromptCaching,
    bool OneHourCache,
    AnthropicThinkingMode ThinkingMode,
    int? MaxRetries,
    TimeSpan? Timeout,
    ThinkingMismatchMode ThinkingMismatch = ThinkingMismatchMode.DropBlock)
{
    public const string RefusalFallbackKey = "RefusalFallback";
    public const string PromptCachingKey = "PromptCaching";
    public const string ThinkingModeKey = "ThinkingMode";
    public const string MaxRetriesKey = "MaxRetries";
    public const string TimeoutSecondsKey = "TimeoutSeconds";
    public const string ThinkingMismatchKey = "ThinkingMismatch";

    // The API accepts at most three fallback entries.
    public const int MaxFallbackModels = 3;

    private static readonly string[] Disabled = ["none", "off", "false", "disabled", "no"];

    // Server-side fallbacks and thinking block binding are only served by the beta messages endpoint.
    public bool UsesBetaEndpoint => RefusalFallback != RefusalFallbackMode.None || ThinkingMismatch != ThinkingMismatchMode.Off;

    public static AnthropicSettings From(AiProviderOptions options)
    {
        var settings = options.Settings;

        var fallback = RefusalFallbackMode.Default;
        IReadOnlyList<string> fallbackModels = [];
        var fallbackValue = Get(settings, RefusalFallbackKey);
        if (fallbackValue is not null)
        {
            if (IsDisabled(fallbackValue))
            {
                fallback = RefusalFallbackMode.None;
            }
            else if (!fallbackValue.Equals("default", StringComparison.OrdinalIgnoreCase)
                && !fallbackValue.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && !fallbackValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                fallbackModels = fallbackValue
                    .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxFallbackModels)
                    .ToArray();
                fallback = fallbackModels.Count > 0 ? RefusalFallbackMode.Models : RefusalFallbackMode.Default;
            }
        }

        var caching = true;
        var oneHour = false;
        if (Get(settings, PromptCachingKey) is { } cachingValue)
        {
            caching = !IsDisabled(cachingValue);
            oneHour = caching && cachingValue.Equals("1h", StringComparison.OrdinalIgnoreCase);
        }

        var thinkingMode = Get(settings, ThinkingModeKey) is { } mode && mode.Equals("extended", StringComparison.OrdinalIgnoreCase)
            ? AnthropicThinkingMode.Extended
            : AnthropicThinkingMode.Adaptive;

        int? maxRetries = Get(settings, MaxRetriesKey) is { } retries
            && int.TryParse(retries, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r >= 0
                ? r
                : null;

        TimeSpan? timeout = Get(settings, TimeoutSecondsKey) is { } seconds
            && int.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s > 0
                ? TimeSpan.FromSeconds(s)
                : null;

        var mismatch = Get(settings, ThinkingMismatchKey) switch
        {
            null => ThinkingMismatchMode.DropBlock,
            var value when IsDisabled(value) => ThinkingMismatchMode.Off,
            var value when value.Equals("error", StringComparison.OrdinalIgnoreCase) => ThinkingMismatchMode.Error,
            _ => ThinkingMismatchMode.DropBlock,
        };

        return new AnthropicSettings(fallback, fallbackModels, caching, oneHour, thinkingMode, maxRetries, timeout, mismatch);
    }

    private static bool IsDisabled(string value) => Disabled.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static string? Get(Dictionary<string, string> settings, string key)
    {
        foreach (var (candidate, value) in settings)
        {
            if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        return null;
    }
}
