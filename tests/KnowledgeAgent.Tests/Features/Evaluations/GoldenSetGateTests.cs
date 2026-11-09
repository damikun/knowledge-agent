using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Evaluations.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Evaluations;

// Ingestion, chunking and hybrid retrieval of the golden set on deterministic test embeddings, and the citation
// plumbing with a rule-based agent. Answer quality needs a real model and runs through `make eval`.
// The minimums are gate.offline in dataset.json, a few points below what the test embeddings score.
public sealed class GoldenSetGateTests(EvalContainers containers) : IClassFixture<EvalContainers>
{
    public const string DatasetName = "orbitwell-golden";

    [Fact]
    public void The_offline_gate_covers_retrieval_and_citations()
    {
        var minimums = EvalDatasetLoader.LoadEmbedded().Single(dataset => dataset.Name == DatasetName).Gate.OfflineMinimums;

        minimums.Keys.ShouldBe(
            [
                EvalMetrics.RetrievalRecallAt5, EvalMetrics.RetrievalRecallAt10, EvalMetrics.RetrievalMrrAt10,
                EvalMetrics.AgentSourceRecall, EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationValidity,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task The_golden_set_passes_the_retrieval_gate()
    {
        var result = await RunAndAssertGateAsync(storage: null);

        result.Items.Count.ShouldBe(EvalDatasetLoader.LoadEmbedded().Single(dataset => dataset.Name == DatasetName).Items.Count);
    }

    [Fact]
    public async Task The_golden_set_passes_the_retrieval_gate_against_postgres_and_clickhouse()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);

        await RunAndAssertGateAsync(await containers.CreateStorageAsync());
    }

    private static async Task<EvalRunResult> RunAndAssertGateAsync(IReadOnlyDictionary<string, string?>? storage)
    {
        var ct = TestContext.Current.CancellationToken;
        var dataset = EvalDatasetLoader.LoadEmbedded().Single(candidate => candidate.Name == DatasetName);
        await using var target = await IsolatedEvalTarget.CreateAsync(EvalTestHost.TestModels(storage), dataset.Corpus, ct);

        var result = await new EvalRunner(NullLogger.Instance).RunAsync(
            new EvalRunPlan(Guid.NewGuid(), "ci gate", dataset, Judge: false, Profile: null, Parallelism: 4, TopK: 10),
            target,
            [],
            cancellationToken: ct);

        result.FailedItems.ShouldBe(0, "an item that fails leaves its scores out");
        var minimums = dataset.Gate.OfflineMinimums;
        minimums.ShouldNotBeEmpty();
        var misses = minimums
            .Where(minimum => !result.Metrics.TryGetValue(minimum.Key, out var value) || value.Mean + 1e-9 < minimum.Value)
            .Select(minimum => $"{minimum.Key}: {(result.Metrics.TryGetValue(minimum.Key, out var value) ? $"{value.Mean:P1}" : "not measured")} is below {minimum.Value:P0}")
            .ToList();
        misses.ShouldBeEmpty(string.Join(Environment.NewLine, result.Metrics.Select(metric => $"{metric.Key} = {metric.Value.Mean:P1}")));
        return result;
    }
}
