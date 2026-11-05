using KnowledgeAgent.Features.Evaluations.Datasets;

namespace KnowledgeAgent.Features.Evaluations;

public static class EvalItemFilter
{
    public static IReadOnlyList<EvalItem> Apply(IReadOnlyList<EvalItem> items, IReadOnlyCollection<string>? tags, int? maxItems)
    {
        IEnumerable<EvalItem> selected = items;
        if (tags is { Count: > 0 })
        {
            selected = selected.Where(item => item.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
        }

        if (maxItems is { } max)
        {
            selected = selected.Take(Math.Max(0, max));
        }

        return selected.ToList();
    }
}
