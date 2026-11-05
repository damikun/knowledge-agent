using System.Globalization;
using KnowledgeAgent.Features.Evaluations.Datasets;

namespace KnowledgeAgent.Features.Evaluations.Gate;

public static class QualityGate
{
    public const string FailedItemsMetric = "failed_items";
    public const string KindMinimum = "minimum";
    public const string KindMaximum = "maximum";
    public const string KindRegression = "regression";
    public const string BaselineCheck = "baseline";

    private const double Epsilon = 1e-9;

    public static EvalGateDto Evaluate(
        EvalGateConfig config,
        IReadOnlyDictionary<string, EvalMetricValueDto> metrics,
        int failedItems,
        EvalBaseline? baseline,
        int? evaluatedItems = null,
        int? datasetItems = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(metrics);

        var checks = new List<EvalGateCheckDto>
        {
            new(
                FailedItemsMetric,
                KindMaximum,
                failedItems,
                0,
                failedItems == 0,
                failedItems == 0 ? "No item failed." : $"{failedItems} item(s) failed (agent errors), so their scores are incomplete."),
        };

        foreach (var (metric, minimum) in config.Minimums.OrderBy(entry => Order(entry.Key)))
        {
            // Unmeasured metrics skip their minimum: judge metrics are absent when the judge is off.
            if (!metrics.TryGetValue(metric, out var value))
            {
                continue;
            }

            var passed = value.Mean + Epsilon >= minimum;
            checks.Add(new EvalGateCheckDto(
                metric,
                KindMinimum,
                value.Mean,
                minimum,
                passed,
                $"{metric} {Percent(value.Mean)} {(passed ? "≥" : "<")} minimum {Percent(minimum)}."));
        }

        if (baseline is not null && evaluatedItems is { } evaluated && datasetItems is { } total && evaluated < total)
        {
            // Means over a subset of the items are not comparable with the whole-set baseline.
            checks.Add(new EvalGateCheckDto(BaselineCheck, KindRegression, evaluated, total, true,
                $"Not compared with the baseline: the run evaluated {evaluated} of {total} items."));
            baseline = null;
        }
        else if (baseline is not null && evaluatedItems is { } count && baseline.Items > 0 && baseline.Items != count)
        {
            checks.Add(new EvalGateCheckDto(BaselineCheck, KindRegression, count, baseline.Items, false,
                $"The baseline was recorded on {baseline.Items} items but the dataset now has {count}; accept the current set with --update-baseline."));
            baseline = null;
        }

        if (baseline is not null)
        {
            var compared = config.RegressionMetrics ?? baseline.Metrics.Keys.ToList();
            foreach (var metric in compared.Where(baseline.Metrics.ContainsKey).OrderBy(Order))
            {
                var expected = Math.Max(0, baseline.Metrics[metric] - config.MaxRegression);

                // A metric the baseline has but the run lacks fails: something stopped working.
                if (!metrics.TryGetValue(metric, out var value))
                {
                    checks.Add(new EvalGateCheckDto(metric, KindRegression, null, expected, false,
                        $"{metric} was not measured, but the baseline has {Percent(baseline.Metrics[metric])}."));
                    continue;
                }

                var passed = value.Mean + Epsilon >= expected;
                var delta = value.Mean - baseline.Metrics[metric];
                checks.Add(new EvalGateCheckDto(
                    metric,
                    KindRegression,
                    value.Mean,
                    expected,
                    passed,
                    $"{metric} {Percent(value.Mean)} vs baseline {Percent(baseline.Metrics[metric])} ({Points(delta)}; allowed drop {Points(-config.MaxRegression)})."));
            }
        }

        return new EvalGateDto(checks.All(check => check.Passed), baseline?.Signature, checks);
    }

    private static int Order(string metric)
    {
        var index = EvalMetrics.All.ToList().IndexOf(metric);
        return index < 0 ? int.MaxValue : index;
    }

    public static string Percent(double value) => value.ToString("0.0%", CultureInfo.InvariantCulture);

    public static string Points(double delta) =>
        (delta >= 0 ? "+" : "−") + Math.Abs(delta * 100).ToString("0.0", CultureInfo.InvariantCulture) + " pts";
}
