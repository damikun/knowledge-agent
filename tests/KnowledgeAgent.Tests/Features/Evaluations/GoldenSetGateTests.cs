using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Evaluations.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Evaluations;

// Ingestion, chunking and hybrid retrieval of the golden set on deterministic test embeddings.
// Answer and citation quality need a real model and run through `make eval`.
public sealed class GoldenSetGateTests(EvalContainers containers) : IClassFixture<EvalContainers>
{
    public const string DatasetName = "orbitwell-golden";

    // Five points below what the TestKit embeddings score, so only a real regression fails.
    private static readonly Dictionary<string, double> Minimums = new(StringComparer.Ordinal)
    {
        [EvalMetrics.RetrievalRecallAt5] = 0.85,
        [EvalMetrics.RetrievalRecallAt10] = 0.93,
        [EvalMetrics.RetrievalMrrAt10] = 0.78,
        [EvalMetrics.AgentSourceRecall] = 0.87,
    };

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
        var misses = Minimums
            .Where(minimum => result.Metrics[minimum.Key].Mean < minimum.Value)
            .Select(minimum => $"{minimum.Key}: {result.Metrics[minimum.Key].Mean:P1} is below {minimum.Value:P0}")
            .ToList();
        misses.ShouldBeEmpty(string.Join(Environment.NewLine, Minimums.Keys.Select(key => $"{key} = {result.Metrics[key].Mean:P1}")));
        return result;
    }
}
