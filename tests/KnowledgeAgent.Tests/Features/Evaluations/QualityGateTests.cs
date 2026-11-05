using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Gate;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class QualityGateTests
{
    private static readonly EvalGateConfig Config = new(
        new Dictionary<string, double> { [EvalMetrics.CitationValidity] = 0.99, [EvalMetrics.JudgeFaithfulness] = 0.8 },
        0.05,
        null);

    [Fact]
    public void Passes_when_minimums_hold_and_nothing_regressed_beyond_the_tolerance()
    {
        var gate = QualityGate.Evaluate(
            Config,
            Metrics((EvalMetrics.CitationValidity, 1), (EvalMetrics.RetrievalRecallAt5, 0.86)),
            failedItems: 0,
            Baseline((EvalMetrics.RetrievalRecallAt5, 0.9)));

        gate.Passed.ShouldBeTrue();
        gate.BaselineSignature.ShouldBe("sig");
        gate.Checks.Select(check => (check.Metric, check.Kind)).ShouldBe(
        [
            (QualityGate.FailedItemsMetric, QualityGate.KindMaximum),
            (EvalMetrics.CitationValidity, QualityGate.KindMinimum),
            (EvalMetrics.RetrievalRecallAt5, QualityGate.KindRegression),
        ]);
    }

    [Fact]
    public void Fails_on_a_regression_beyond_the_tolerance()
    {
        var gate = QualityGate.Evaluate(Config, Metrics((EvalMetrics.RetrievalRecallAt5, 0.84)), 0, Baseline((EvalMetrics.RetrievalRecallAt5, 0.9)));

        gate.Passed.ShouldBeFalse();
        var check = gate.Checks.Single(check => check.Kind == QualityGate.KindRegression);
        check.Expected.ShouldBe(0.85, 1e-9);
        check.Message.ShouldContain("−6.0 pts");
    }

    [Fact]
    public void Fails_on_failed_items_and_unmet_minimums_but_skips_minimums_that_were_not_measured()
    {
        var gate = QualityGate.Evaluate(Config, Metrics((EvalMetrics.CitationValidity, 0.9)), failedItems: 2, baseline: null);

        gate.Passed.ShouldBeFalse();
        gate.Checks.Single(check => check.Metric == QualityGate.FailedItemsMetric).Passed.ShouldBeFalse();
        gate.Checks.Single(check => check.Metric == EvalMetrics.CitationValidity).Passed.ShouldBeFalse();
        gate.Checks.ShouldNotContain(check => check.Metric == EvalMetrics.JudgeFaithfulness);
    }

    [Fact]
    public void A_metric_the_baseline_has_but_the_run_lacks_fails()
    {
        var gate = QualityGate.Evaluate(Config, Metrics(), 0, Baseline((EvalMetrics.AnswerFactRecall, 0.7)));

        gate.Passed.ShouldBeFalse();
        gate.Checks.Single(check => check.Metric == EvalMetrics.AnswerFactRecall).Actual.ShouldBeNull();
    }

    [Fact]
    public void A_subset_run_is_not_compared_with_the_whole_set_baseline()
    {
        var gate = QualityGate.Evaluate(Config, Metrics((EvalMetrics.RetrievalRecallAt5, 0.2)), 0, Baseline((EvalMetrics.RetrievalRecallAt5, 0.9)), evaluatedItems: 5, datasetItems: 30);

        gate.Passed.ShouldBeTrue();
        gate.BaselineSignature.ShouldBeNull();
        var note = gate.Checks.Single(check => check.Metric == QualityGate.BaselineCheck);
        note.Passed.ShouldBeTrue();
        note.Message.ShouldContain("5 of 30 items");
        gate.Checks.ShouldNotContain(check => check.Metric == EvalMetrics.RetrievalRecallAt5);
    }

    [Fact]
    public void A_full_run_over_a_changed_golden_set_asks_for_a_new_baseline()
    {
        var gate = QualityGate.Evaluate(Config, Metrics((EvalMetrics.RetrievalRecallAt5, 0.9)), 0, Baseline((EvalMetrics.RetrievalRecallAt5, 0.9)), evaluatedItems: 31, datasetItems: 31);

        gate.Passed.ShouldBeFalse();
        gate.Checks.Single(check => check.Metric == QualityGate.BaselineCheck).Message.ShouldContain("--update-baseline");
    }

    [Fact]
    public void Signatures_identify_embeddings_agent_model_and_judge()
    {
        var settings = new EvalRunSettings(EvalTargets.Isolated, "orchestrator", "anthropic", "claude-opus-5-5", "openai:text-embedding-3-small:1536", "anthropic/claude-opus-5-5", null, null, null, 3, 10);

        EvalSignature.Create(settings, judge: false).ShouldBe("openai-text-embedding-3-small-1536__anthropic-claude-opus-5-5");
        EvalSignature.Create(settings, judge: true).ShouldBe("openai-text-embedding-3-small-1536__anthropic-claude-opus-5-5__judge-anthropic-claude-opus-5-5");

        var reranked = settings with { RerankModel = "voyage/rerank-2.5" };
        EvalSignature.Create(reranked, judge: true).ShouldBe("openai-text-embedding-3-small-1536__anthropic-claude-opus-5-5__rerank-voyage-rerank-2.5__judge-anthropic-claude-opus-5-5");
        EvalSignature.Create(settings with { SearchMode = SearchModes.Vector }, judge: false)
            .ShouldBe(EvalSignature.Create(settings, judge: false), "search modes are compared against the same baseline");
    }

    [Fact]
    public void Baselines_round_trip_through_the_dataset_folder()
    {
        var directory = Directory.CreateTempSubdirectory("eval-baseline-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "dataset.json"), """{"name":"tiny","items":[{"id":"a","question":"q?"}]}""");
            BaselineStore.Write(directory, "sig-1", Metrics((EvalMetrics.RetrievalRecallAt5, 0.912345)), 1, DateTimeOffset.UnixEpoch);

            var dataset = EvalDatasetLoader.LoadDirectory(directory);

            dataset.Baselines["sig-1"].Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(0.9123);
            dataset.Baselines["sig-1"].Items.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Aggregates_average_each_metric_over_the_items_that_have_it()
    {
        var items = new[]
        {
            Result("a", ["hr"], (EvalMetrics.AnswerFactRecall, 1), (EvalMetrics.CitationValidity, 1)),
            Result("b", ["hr", "multi"], (EvalMetrics.AnswerFactRecall, 0.5)),
            Result("c", ["multi"], (EvalMetrics.AnswerAbstained, 0)),
        };

        var metrics = MetricAggregator.Aggregate(items);
        metrics[EvalMetrics.AnswerFactRecall].ShouldBe(new EvalMetricValueDto(0.75, 2));
        metrics[EvalMetrics.CitationValidity].ShouldBe(new EvalMetricValueDto(1, 1));
        metrics[EvalMetrics.AnswerAbstained].ShouldBe(new EvalMetricValueDto(0, 1));

        var byTag = MetricAggregator.ByTag(items);
        byTag.Select(tag => (tag.Tag, tag.Items)).ShouldBe([("hr", 2), ("multi", 2)]);
        byTag[1].Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(new EvalMetricValueDto(0.5, 1));
    }

    private static IReadOnlyDictionary<string, EvalMetricValueDto> Metrics(params (string Metric, double Mean)[] values) =>
        values.ToDictionary(value => value.Metric, value => new EvalMetricValueDto(value.Mean, 10));

    private static EvalBaseline Baseline(params (string Metric, double Mean)[] values) =>
        new("sig", values.ToDictionary(value => value.Metric, value => value.Mean), 10, null);

    private static EvalItemResultDto Result(string id, string[] tags, params (string Metric, double Value)[] metrics) => new(
        id, "q", true, tags, [], [], null, EvalItemStatuses.Completed, "a", "stop", null,
        metrics.ToDictionary(metric => metric.Metric, metric => metric.Value), [], [], [], null, 1, null, null, 0, null, null);
}
