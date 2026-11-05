using Microsoft.Extensions.AI;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class UsageAccumulator
{
    private readonly Lock _gate = new();
    private long? _input;
    private long? _output;
    private long? _total;

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
        }
    }

    public UsageTotals Snapshot()
    {
        lock (_gate)
        {
            return new UsageTotals(_input, _output, _total);
        }
    }

    private static long? Sum(long? current, long? value) => value is null ? current : (current ?? 0) + value.Value;
}

internal readonly record struct UsageTotals(long? InputTokens, long? OutputTokens, long? TotalTokens);
