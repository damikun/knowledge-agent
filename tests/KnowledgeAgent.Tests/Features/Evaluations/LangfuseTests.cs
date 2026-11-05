using System.Net;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.Langfuse;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using static KnowledgeAgent.Tests.Features.Evaluations.Support.EvalTestHost;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class LangfuseTests : IAsyncLifetime
{
    private FakeLangfuseServer _server = null!;
    private HttpClient _http = null!;
    private LangfuseEvalClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _server = await FakeLangfuseServer.StartAsync();
        _http = new HttpClient();
        LangfuseEvalClient.ConfigureHttpClient(_http, new LangfuseOptions
        {
            BaseUrl = _server.BaseAddress.ToString(),
            PublicKey = _server.PublicKey,
            SecretKey = _server.SecretKey,
        });
        _client = new LangfuseEvalClient(_http);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Sync_creates_the_dataset_and_only_rewrites_items_that_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataset = SmallDataset(
            Item("days", "How many vacation days?", ["vacation.md"], ["27 vacation days"], ["hr"], reference: "27."),
            Item("ceo", "Who is the CEO?", answerable: false));

        var first = await LangfuseDatasets.SyncAsync(_client, dataset, ct);

        (first.Created, first.Updated, first.Unchanged, first.Archived).ShouldBe((2, 0, 0, 0));
        _server.Datasets["small-golden"]["metadata"]!["source"]!.GetValue<string>().ShouldBe("knowledge-agent");
        var items = _server.DatasetItems("small-golden");
        items.Keys.Order().ShouldBe(["small-golden:ceo", "small-golden:days"]);
        items["small-golden:days"]["input"]!["question"]!.GetValue<string>().ShouldBe("How many vacation days?");
        items["small-golden:days"]["expectedOutput"]!["facts"]!.AsArray().Single()!.GetValue<string>().ShouldBe("27 vacation days");
        items["small-golden:ceo"]["expectedOutput"]!["answerable"]!.GetValue<bool>().ShouldBeFalse();
        first.ItemsByLocalId["days"].Id.ShouldBe("small-golden:days");
        first.ItemsByLocalId["days"].Version.ShouldNotBeNull();

        var writes = _server.DatasetItemWrites;
        var second = await LangfuseDatasets.SyncAsync(_client, dataset, ct);
        (second.Created, second.Updated, second.Unchanged, second.Archived).ShouldBe((0, 0, 2, 0));
        _server.DatasetItemWrites.ShouldBe(writes);

        var edited = dataset.With([dataset.Items[0] with { Question = "How many vacation days per year?" }]);
        var third = await LangfuseDatasets.SyncAsync(_client, edited, ct);
        (third.Created, third.Updated, third.Unchanged, third.Archived).ShouldBe((0, 1, 0, 1));
        _server.DatasetItems("small-golden")["small-golden:ceo"]["status"]!.GetValue<string>().ShouldBe("ARCHIVED");
    }

    [Fact]
    public void Langfuse_items_become_eval_items_from_strings_or_objects()
    {
        var dataset = new LangfuseDataset("ds-1", "support-questions", "From traces", null);
        LangfuseDatasetItem Item(string id, JsonNode? input, JsonNode? expected, JsonNode? metadata = null, string status = "ACTIVE") =>
            new(id, "support-questions", "ds-1", status, input, expected, metadata, "2026-10-01T00:00:00Z", null);

        var (evalDataset, warnings) = LangfuseDatasets.ToEvalDataset(dataset,
        [
            Item("t1", JsonValue.Create("How do I reset my password?"), JsonValue.Create("Use the self-service portal.")),
            Item("t2", new JsonObject { ["message"] = "What is the SLA?" }, new JsonObject
            {
                ["answer"] = "Four hours.",
                ["facts"] = new JsonArray("4 hours"),
                ["sources"] = new JsonArray("Support SLA"),
            }, new JsonObject { ["tags"] = new JsonArray("support") }),
            Item("t3", new JsonObject { ["question"] = "Stock ticker?" }, new JsonObject { ["answerable"] = false, ["facts"] = new JsonArray("x") }),
            Item("t4", new JsonObject { ["other"] = 1 }, null),
            Item("t5", JsonValue.Create("Archived?"), null, status: "ARCHIVED"),
        ]);

        evalDataset.Source.ShouldBe(EvalDatasetSources.Langfuse);
        evalDataset.LangfuseDatasetId.ShouldBe("ds-1");
        evalDataset.Items.Select(item => item.Id).ShouldBe(["t1", "t2", "t3"]);
        evalDataset.Items[0].ShouldSatisfyAllConditions(
            item => item.Question.ShouldBe("How do I reset my password?"),
            item => item.ReferenceAnswer.ShouldBe("Use the self-service portal."),
            item => item.LangfuseItemId.ShouldBe("t1"),
            item => item.LangfuseItemVersion.ShouldBe("2026-10-01T00:00:00Z"));
        evalDataset.Items[1].ExpectedFacts.ShouldBe(["4 hours"]);
        evalDataset.Items[1].ExpectedSources.ShouldBe(["Support SLA"]);
        evalDataset.Items[1].Tags.ShouldBe(["support"]);
        evalDataset.Items[2].Answerable.ShouldBeFalse();
        evalDataset.Items[2].ExpectedFacts.ShouldBeEmpty();
        warnings.ShouldHaveSingleItem().ShouldContain("t4");
    }

    [Fact]
    public async Task The_observer_sends_item_scores_on_the_root_observation_and_run_scores_on_the_experiment()
    {
        var ct = TestContext.Current.CancellationToken;
        var observer = new LangfuseExperimentObserver(_client, NullLogger.Instance);
        var plan = Plan();
        var item = Result("days", traceId: "0af7651916cd43dd8448eb211c80319c", (EvalMetrics.AnswerFactRecall, 0.5), (EvalMetrics.CitationValidity, 1));

        await observer.OnItemCompletedAsync(plan, new EvalItemOutcome(item, "b7ad6b7169203331"), ct);
        await observer.OnRunCompletedAsync(RunResult(plan, item), ct);

        var scores = _server.Scores;
        scores.Count.ShouldBe(4);
        var facts = scores.Single(score => score["name"]!.GetValue<string>() == EvalMetrics.AnswerFactRecall && score["traceId"] is not null);
        facts["traceId"]!.GetValue<string>().ShouldBe("0af7651916cd43dd8448eb211c80319c");
        facts["observationId"]!.GetValue<string>().ShouldBe("b7ad6b7169203331");
        facts["value"]!.GetValue<double>().ShouldBe(0.5);
        facts["dataType"]!.GetValue<string>().ShouldBe("NUMERIC");
        facts["environment"]!.GetValue<string>().ShouldBe("evaluation");
        facts["comment"]!.GetValue<string>().ShouldBe("Missing: Q-417");
        facts["id"]!.GetValue<string>().ShouldBe(LangfuseExperimentObserver.ScoreId("exp-1", "days", EvalMetrics.AnswerFactRecall));

        var runScore = scores.Single(score => score["datasetRunId"] is not null && score["name"]!.GetValue<string>() == EvalMetrics.AnswerFactRecall);
        runScore["datasetRunId"]!.GetValue<string>().ShouldBe("exp-1");
        runScore["traceId"].ShouldBeNull();
        observer.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejected_scores_become_warnings_instead_of_failures()
    {
        _server.RejectScore = score => score["datasetRunId"] is not null ? "run-level scores are not supported" : null;
        var observer = new LangfuseExperimentObserver(_client, NullLogger.Instance);
        var plan = Plan();

        await observer.OnRunCompletedAsync(RunResult(plan, Result("days", "0af7651916cd43dd8448eb211c80319c", (EvalMetrics.AnswerFactRecall, 1))), TestContext.Current.CancellationToken);

        observer.Warnings.ShouldHaveSingleItem().ShouldContain("run-level scores are not supported");
    }

    [Fact]
    public async Task Api_errors_carry_the_status_and_message()
    {
        _server.Unavailable = true;

        var error = await Should.ThrowAsync<LangfuseEvalException>(() => _client.GetDatasetAsync("x", TestContext.Current.CancellationToken));

        error.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        error.Message.ShouldContain("GET api/public/v2/datasets/x");
    }

    private static EvalRunPlan Plan() => new(
        Guid.NewGuid(),
        "run",
        SmallDataset(Item("days", "q")),
        false,
        null,
        1,
        10,
        new EvalExperimentIdentity("exp-1", "run", "ds-1", null, "evaluation", new Dictionary<string, string>()));

    private static EvalItemResultDto Result(string id, string traceId, params (string Metric, double Value)[] metrics) => new(
        id, "q", true, [], [], ["Q-417"], null, EvalItemStatuses.Completed, "answer", "stop", null,
        metrics.ToDictionary(metric => metric.Metric, metric => metric.Value), [], [], ["Q-417"], null, 1, null, null, 0, traceId, null);

    private static EvalRunResult RunResult(EvalRunPlan plan, params EvalItemResultDto[] items) => new(
        plan,
        new EvalRunSettings(EvalTargets.Isolated, "orchestrator", "fake", "m", "e", null, null, null, null, 1, 10),
        items,
        MetricAggregator.Aggregate(items),
        [],
        [],
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        false);
}
