using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using KnowledgeAgent.Api.Tests.Support;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Conversations;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.Hosting;
using KnowledgeAgent.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Api.Tests.Scenarios;

public abstract class EvalScenarios(AppFixture fixture)
{
    private const string Golden = "orbitwell-golden";

    private const string Verdicts = """
        {"faithfulness":{"reasoning":"Supported.","verdict":"yes","issues":[]},
         "relevance":{"reasoning":"Direct.","verdict":"yes","issues":[]},
         "correctness":{"reasoning":"Close.","verdict":"partially","issues":["incomplete"]}}
        """;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(90);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Datasets_list_builtin_sets_langfuse_datasets_and_judge_availability()
    {
        var app = await fixture.GetAppAsync();
        app.Langfuse.AddDataset("support-questions", "Questions from production traces");
        app.Langfuse.AddDatasetItem("support-questions", "sq-1", JsonValue.Create("How do I reset my password?"));
        app.Langfuse.AddDatasetItem("support-questions", "sq-2", JsonValue.Create("Archived question"), status: "ARCHIVED");

        var list = await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets");

        list.LangfuseConfigured.ShouldBeTrue();
        list.LangfuseError.ShouldBeNull();
        list.JudgeAvailable.ShouldBeFalse("the judge profile has no model in tests");
        list.JudgeModel.ShouldBeNull();
        var golden = list.Datasets.Single(dataset => dataset.Name == Golden);
        golden.Source.ShouldBe(EvalDatasetSources.Builtin);
        golden.ItemCount.ShouldBeGreaterThan(0);
        golden.CorpusDocuments.ShouldBeGreaterThan(0);
        golden.Tags.ShouldContain("unanswerable");
        var support = list.Datasets.Single(dataset => dataset.Name == "support-questions");
        support.Source.ShouldBe(EvalDatasetSources.Langfuse);
        support.ItemCount.ShouldBe(1);
        support.LangfuseUrl.ShouldNotBeNull().ShouldContain("/project/fake-project/datasets/");
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Syncing_a_builtin_set_creates_the_langfuse_dataset_once()
    {
        var app = await fixture.GetAppAsync();
        var golden = (await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets")).Datasets.Single(dataset => dataset.Name == Golden);

        using var first = await app.Client.PostAsync($"/api/evals/datasets/{Golden}/sync", content: null, Ct);
        var created = await first.ReadJsonAsync<EvalDatasetSyncResultDto>();
        using var second = await app.Client.PostAsync($"/api/evals/datasets/{Golden}/sync", content: null, Ct);
        var again = await second.ReadJsonAsync<EvalDatasetSyncResultDto>();
        using var unknown = await app.Client.PostAsync("/api/evals/datasets/nope/sync", content: null, Ct);

        (created.Created + created.Unchanged).ShouldBe(golden.ItemCount);
        again.Unchanged.ShouldBe(golden.ItemCount);
        again.Created.ShouldBe(0);
        app.Langfuse.DatasetItems(Golden).Count.ShouldBe(golden.ItemCount);
        again.LangfuseUrl.ShouldNotBeNull();
        await unknown.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_builtin_run_executes_in_an_isolated_knowledge_base_and_records_a_langfuse_experiment()
    {
        var app = await fixture.GetAppAsync();
        var sourcesBefore = (await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats")).Total;

        using var start = await app.Client.PostJsonAsync("/api/evals/runs", new StartEvalRunRequest(Golden, MaxItems: 3, Name: "api smoke"));
        var queued = await start.ReadJsonAsync<EvalRunSummaryDto>(HttpStatusCode.Accepted);
        start.Headers.Location!.ToString().ShouldBe($"/api/evals/runs/{queued.Id}");
        queued.Status.ShouldBeOneOf(EvalRunStatuses.Queued, EvalRunStatuses.Running);
        queued.TotalItems.ShouldBe(3);

        var run = await WaitForRunAsync(app, queued.Id);

        run.Summary.Status.ShouldBe(EvalRunStatuses.Completed, run.Summary.Error);
        run.Summary.Name.ShouldBe("api smoke");
        run.Summary.CompletedItems.ShouldBe(3);
        run.Summary.FailedItems.ShouldBe(0);
        run.Summary.ChatModel.ShouldBe($"{FakeModels.ProviderKey}/{FakeModels.ChatModel}");
        run.Summary.EmbeddingModel.ShouldBe($"fake:{FakeModels.EmbeddingModel}:{FakeModels.EmbeddingDimensions}");
        run.Summary.Metrics.ShouldContainKey(EvalMetrics.RetrievalRecallAt5);
        run.Summary.Metrics[EvalMetrics.CitationValidity].Mean.ShouldBe(1);
        run.Summary.Gate.ShouldNotBeNull().Passed.ShouldBeTrue(string.Join("; ", run.Summary.Gate.Checks.Select(check => check.Message)));
        run.Config.Target.ShouldBe("isolated");
        run.Config.PromptName.ShouldBe("orchestrator-system");
        run.Items.Count.ShouldBe(3);
        run.Items.ShouldAllBe(item => item.Status == "completed" && item.Answer != null && item.TraceId != null);
        run.Warnings.ShouldBeEmpty();

        run.Summary.LangfuseUrl.ShouldNotBeNull().ShouldContain($"/runs/{queued.Id:D}");
        app.Langfuse.DatasetItems(Golden).ShouldNotBeEmpty();
        (await EventuallyAsync(() => app.Langfuse.Scores.Any(score => score["datasetRunId"]?.GetValue<string>() == queued.Id.ToString("D")))).ShouldBeTrue();
        var itemScores = app.Langfuse.Scores.Where(score => run.Items.Any(item => item.TraceId == score["traceId"]?.GetValue<string>())).ToList();
        itemScores.Select(score => score["traceId"]!.GetValue<string>()).Distinct().Count().ShouldBe(3);
        itemScores.ShouldAllBe(score => score["observationId"] != null && score["environment"]!.GetValue<string>() == "evaluation");

        (await app.Client.GetJsonAsync<SourceStatsDto>("/api/sources/stats")).Total.ShouldBe(sourcesBefore);
        var runs = await app.Client.GetJsonAsync<EvalRunSummaryDto[]>($"/api/evals/runs?dataset={Golden}");
        runs.ShouldContain(summary => summary.Id == queued.Id);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Invalid_runs_are_rejected_with_field_errors()
    {
        var app = await fixture.GetAppAsync();

        await ExpectInvalidAsync(app, new StartEvalRunRequest("nope"), "dataset");
        await ExpectInvalidAsync(app, new StartEvalRunRequest(Golden, Source: "spreadsheet"), "source");
        await ExpectInvalidAsync(app, new StartEvalRunRequest(Golden, MaxItems: 0), "maxItems");
        await ExpectInvalidAsync(app, new StartEvalRunRequest(Golden, Tags: ["no-such-tag"]), "tags");
        await ExpectInvalidAsync(app, new StartEvalRunRequest(Golden, Profile: "no-such-profile"), "profile");
        var judge = await ExpectInvalidAsync(app, new StartEvalRunRequest(Golden, Judge: true), "judge");
        judge.FieldErrors["judge"].Single().ShouldContain("'judge' profile");
        await ExpectInvalidAsync(app, new StartEvalRunRequest("unknown-in-langfuse", Source: "langfuse"), "dataset");

        using var badLimit = await app.Client.GetAsync("/api/evals/runs?limit=0", Ct);
        await badLimit.ReadProblemAsync(HttpStatusCode.BadRequest);
        using var missing = await app.Client.GetAsync($"/api/evals/runs/{Guid.NewGuid()}", Ct);
        await missing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_running_run_can_be_cancelled_and_only_finished_runs_can_be_deleted()
    {
        await using var app = await fixture.CreateAppAsync();
        var model = app.ChatModels.Use("orchestrator", new HangingChatClient());

        using var start = await app.Client.PostJsonAsync("/api/evals/runs", new StartEvalRunRequest(Golden, MaxItems: 2));
        var queued = await start.ReadJsonAsync<EvalRunSummaryDto>(HttpStatusCode.Accepted);
        (await EventuallyAsync(() => model.Calls > 0)).ShouldBeTrue("the run never reached the model");

        using var deleteActive = await app.Client.DeleteAsync($"/api/evals/runs/{queued.Id}", Ct);
        await deleteActive.ReadProblemAsync(HttpStatusCode.Conflict);

        using var cancel = await app.Client.PostAsync($"/api/evals/runs/{queued.Id}/cancel", content: null, Ct);
        await cancel.ReadJsonAsync<EvalRunSummaryDto>();
        var cancelled = await WaitForRunAsync(app, queued.Id);
        cancelled.Summary.Status.ShouldBe(EvalRunStatuses.Cancelled);

        using var delete = await app.Client.DeleteAsync($"/api/evals/runs/{queued.Id}", Ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var gone = await app.Client.GetAsync($"/api/evals/runs/{queued.Id}", Ct);
        await gone.ReadProblemAsync(HttpStatusCode.NotFound);
        using var cancelMissing = await app.Client.PostAsync($"/api/evals/runs/{Guid.NewGuid()}/cancel", content: null, Ct);
        await cancelMissing.ReadProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task A_langfuse_dataset_runs_against_the_live_knowledge_base_without_leaving_conversations()
    {
        var app = await fixture.GetAppAsync();
        var word = ApiClient.UniqueWord();
        var title = $"Holiday rules {word}";
        await app.AddReadyTextSourceAsync(title, $"The {word} holiday allowance is 31 days for every employee.");
        var dataset = "live-" + word;
        app.Langfuse.AddDataset(dataset);
        app.Langfuse.AddDatasetItem(
            dataset,
            "live-1",
            new JsonObject { ["question"] = $"What is the {word} holiday allowance?" },
            new JsonObject { ["answer"] = "31 days.", ["facts"] = new JsonArray("31 days"), ["sources"] = new JsonArray(title) },
            new JsonObject { ["tags"] = new JsonArray("hr") });
        var conversationsBefore = (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?limit=100")).Items.Select(conversation => conversation.Id).ToList();

        using var start = await app.Client.PostJsonAsync("/api/evals/runs", new StartEvalRunRequest(dataset, Source: "langfuse"));
        var queued = await start.ReadJsonAsync<EvalRunSummaryDto>(HttpStatusCode.Accepted);
        var run = await WaitForRunAsync(app, queued.Id);

        run.Summary.Status.ShouldBe(EvalRunStatuses.Completed, run.Summary.Error);
        run.Summary.DatasetSource.ShouldBe(EvalDatasetSources.Langfuse);
        run.Config.Target.ShouldBe("live");
        var item = run.Items.Single();
        item.ItemId.ShouldBe("live-1");
        item.Tags.ShouldBe(["hr"]);
        item.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
        item.Metrics[EvalMetrics.CitationSourceRecall].ShouldBe(1);
        run.Warnings.ShouldBeEmpty();
        (await app.Client.GetJsonAsync<ConversationListPageDto>("/api/conversations?limit=100")).Items.Select(conversation => conversation.Id).ShouldBe(conversationsBefore);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task With_an_llm_judge_configured_runs_include_judge_scores()
    {
        await using var app = await fixture.CreateAppAsync(options =>
        {
            options.Settings["AI:Profiles:judge:Provider"] = FakeModels.ProviderKey;
            options.Settings["AI:Profiles:judge:Model"] = "fake-judge";
        });
        app.ChatModels.Use("judge", new ScriptedChatClient { FallbackText = Verdicts });

        var list = await app.Client.GetJsonAsync<EvalDatasetListDto>("/api/evals/datasets");
        list.JudgeAvailable.ShouldBeTrue();
        list.JudgeModel.ShouldBe("fake/fake-judge");

        using var start = await app.Client.PostJsonAsync("/api/evals/runs", new StartEvalRunRequest(Golden, Judge: true, MaxItems: 2));
        var queued = await start.ReadJsonAsync<EvalRunSummaryDto>(HttpStatusCode.Accepted);
        var run = await WaitForRunAsync(app, queued.Id);

        run.Summary.Status.ShouldBe(EvalRunStatuses.Completed, run.Summary.Error);
        run.Summary.Judge.ShouldBeTrue();
        run.Config.JudgeModel.ShouldBe("fake/fake-judge");
        run.Summary.Metrics[EvalMetrics.JudgeFaithfulness].Mean.ShouldBe(1);
        run.Summary.Metrics[EvalMetrics.JudgeCorrectness].Mean.ShouldBe(0.5);
        run.Items.ShouldAllBe(item => item.Judgement != null && item.Judgement.Criteria.Count == 3);
    }

    [Fact(Timeout = TestTimeouts.Default)]
    public async Task Runs_interrupted_by_a_restart_are_settled_at_startup()
    {
        await using var app = await fixture.CreateAppAsync();
        var interrupted = new EvalRunRecord
        {
            Id = Guid.CreateVersion7(),
            Name = "interrupted",
            Dataset = Golden,
            DatasetSource = EvalDatasetSources.Builtin,
            Status = EvalRunStatuses.Running,
            RequestJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEvalRunRepository>().AddAsync(interrupted, Ct);
        }

        var recovery = app.Services.GetServices<IStartupInitializer>().Single(initializer => initializer.Name == "Eval run recovery");
        await recovery.InitializeAsync(Ct);

        // Recovery queues the run's job again, and the job settles the run.
        var run = await WaitForRunAsync(app, interrupted.Id);
        run.Summary.Status.ShouldBe(EvalRunStatuses.Failed);
        run.Summary.Error.ShouldNotBeNull().ShouldContain("restarted");
    }

    private static async Task<ProblemDocument> ExpectInvalidAsync(KnowledgeAgentApp app, StartEvalRunRequest request, string field)
    {
        using var response = await app.Client.PostJsonAsync("/api/evals/runs", request);
        var problem = await response.ReadProblemAsync(HttpStatusCode.BadRequest);
        problem.FieldErrors.ShouldContainKey(field);
        return problem;
    }

    private static async Task<EvalRunDto> WaitForRunAsync(KnowledgeAgentApp app, Guid id)
    {
        var stopwatch = Stopwatch.StartNew();
        EvalRunDto? run = null;
        while (stopwatch.Elapsed < RunTimeout)
        {
            run = await app.Client.GetJsonAsync<EvalRunDto>($"/api/evals/runs/{id}");
            if (EvalRunStatuses.IsFinished(run.Summary.Status))
            {
                return run;
            }

            await Task.Delay(200, Ct);
        }

        throw new ShouldAssertException($"Eval run {id} did not finish within {RunTimeout.TotalSeconds:0}s (status {run?.Summary.Status}).{Environment.NewLine}{app.Logs.Dump()}");
    }

    private static Task<bool> EventuallyAsync(Func<bool> condition) => ApiClient.EventuallyAsync(condition, TimeSpan.FromSeconds(15));

    private sealed class HangingChatClient : IChatClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

public sealed class InMemoryEvalScenarios(InMemoryAppFixture fixture) : EvalScenarios(fixture), IClassFixture<InMemoryAppFixture>;

public sealed class ContainersEvalScenarios(ContainersAppFixture fixture) : EvalScenarios(fixture), IClassFixture<ContainersAppFixture>;
