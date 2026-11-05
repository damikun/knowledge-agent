namespace KnowledgeAgent.Features.Evaluations.Metrics;

public static class MetricAggregator
{
    public static IReadOnlyDictionary<string, EvalMetricValueDto> Aggregate(IEnumerable<EvalItemResultDto> items)
    {
        var list = items.ToList();
        var result = new Dictionary<string, EvalMetricValueDto>(StringComparer.Ordinal);
        foreach (var metric in EvalMetrics.All)
        {
            var values = list.Where(item => item.Metrics.ContainsKey(metric)).Select(item => item.Metrics[metric]).ToList();
            if (values.Count > 0)
            {
                result[metric] = new EvalMetricValueDto(Math.Round(values.Average(), 4), values.Count);
            }
        }

        return result;
    }

    public static IReadOnlyList<EvalTagSummaryDto> ByTag(IEnumerable<EvalItemResultDto> items)
    {
        var list = items.ToList();
        return list
            .SelectMany(item => item.Tags.Select(tag => (tag, item)))
            .GroupBy(entry => entry.tag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EvalTagSummaryDto(group.Key, group.Count(), Aggregate(group.Select(entry => entry.item))))
            .ToList();
    }
}
