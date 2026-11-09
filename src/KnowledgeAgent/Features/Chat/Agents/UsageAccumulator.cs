using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class UsageAccumulator
{
    // The key the Anthropic adapter uses for cache writes, which UsageDetails has no property for.
    public const string CacheCreationInputTokensKey = "CacheCreationInputTokens";

    private readonly Lock _gate = new();
    private long? _input;
    private long? _output;
    private long? _total;
    private long? _cacheRead;
    private long? _cacheWrite;

    public void Add(UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        var total = usage.TotalTokenCount
            ?? (usage.InputTokenCount is null && usage.OutputTokenCount is null
                ? null
                : (usage.InputTokenCount ?? 0) + (usage.OutputTokenCount ?? 0));

        lock (_gate)
        {
            _input = Sum(_input, usage.InputTokenCount);
            _output = Sum(_output, usage.OutputTokenCount);
            _total = Sum(_total, total);
            _cacheRead = Sum(_cacheRead, usage.CachedInputTokenCount);
            _cacheWrite = Sum(_cacheWrite, CacheWriteTokens(usage));
        }
    }

    public UsageTotals Snapshot()
    {
        lock (_gate)
        {
            return new UsageTotals(_input, _output, _total, _cacheRead, _cacheWrite);
        }
    }

    // Input tokens already include both cache counts, on Anthropic as on OpenAI.
    private static long? CacheWriteTokens(UsageDetails usage) =>
        usage.AdditionalCounts is { } counts && counts.TryGetValue(CacheCreationInputTokensKey, out var value) ? value : null;

    private static long? Sum(long? current, long? value) => value is null ? current : (current ?? 0) + value.Value;
}

internal readonly record struct UsageTotals(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    long? CacheReadInputTokens = null,
    long? CacheCreationInputTokens = null);
