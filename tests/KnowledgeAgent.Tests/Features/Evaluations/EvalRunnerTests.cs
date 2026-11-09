using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Gate;
using KnowledgeAgent.Features.Evaluations.Judge;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Infrastructure.Persistence;
using KnowledgeAgent.Infrastructure.Telemetry;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Evaluations.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static KnowledgeAgent.Tests.Features.Evaluations.Support.EvalTestHost;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class EvalRunnerTests
{
    private const string Verdicts = """
        {"faithfulness":{"reasoning":"Supported by S1.","verdict":"yes","issues":[]},
         "relevance":{"reasoning":"Direct.","verdict":"yes","issues":[]},
         "correctness":{"reasoning":"Matches the reference.","verdict":"partially","issues":["misses the carry-over rule"]}}
        """;

    private static readonly EvalItem VacationDays = Item(
        "vacation-days", "How many vacation days does every employee receive per calendar year?", ["vacation.md"], ["27 vacation days"], ["hr"], reference: "27 days.");

    private static readonly EvalItem GripperError = Item(
        "gripper-error", "What does robot error Q-417 mean?", ["robot-errors.md"], ["Q-417"], ["product"]);

    private static readonly EvalItem Ceo = Item("ceo", "Who is the chief executive officer of the company?", tags: ["unanswerable"], answerable: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_scores_every_item_through_the_real_pipeline()
    {
        var dataset = SmallDataset(VacationDays, GripperError, Ceo);
        await using var target = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);
        var observer = new RecordingObserver();

        var result = await Runner().RunAsync(Plan(dataset, parallelism: 2), target, [observer], cancellationToken: Ct);

        result.Cancelled.ShouldBeFalse();
        result.Items.Select(item => item.ItemId).ShouldBe(["vacation-days", "gripper-error", "ceo"]);
        result.Items.ShouldAllBe(item => item.Status == EvalItemStatuses.Completed && item.Answer != null);
        result.Settings.ShouldSatisfyAllConditions(
            settings => settings.Target.ShouldBe(EvalTargets.Isolated),
            settings => settings.ChatModelLabel.ShouldBe("fake/fake-chat"),
            settings => settings.EmbeddingModel.ShouldBe("fake:fake-embedding:64"),
            settings => settings.PromptName.ShouldBe("orchestrator-system"),
            settings => settings.PromptVersion.ShouldNotBeNull(),
            settings => settings.SearchMode.ShouldBe(SearchModes.Hybrid),
            settings => settings.RerankModel.ShouldBeNull("the test preset never reranks"));

        var vacation = result.Items[0];
        vacation.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
        vacation.Metrics[EvalMetrics.CitationValidity].ShouldBe(1);
        // The rule-based test model quotes one line, so answer scores are computed but not meaningful here.
        vacation.Metrics.ShouldContainKey(EvalMetrics.AnswerFactRecall);
        vacation.Retrieved.First().Expected.ShouldBeTrue();
        vacation.ToolCalls.ShouldBe(1);

        result.Items[2].Metrics.ShouldContainKey(EvalMetrics.AnswerAbstained);
        result.Metrics[EvalMetrics.CitationValidity].Mean.ShouldBe(1);
        observer.Started.ShouldBe(1);
        observer.Items.Count.ShouldBe(3);
        observer.Completed.ShouldBe(1);
    }

    [Fact]
    public async Task A_citation_tag_the_agent_never_received_lowers_citation_validity()
    {
        var model = new ScriptedChatClient()
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "vacation days per year" })
            .ThenText("Every employee receives 27 vacation days per calendar year [S1][S9].");
        var dataset = SmallDataset(VacationDays);
        await using var target = await IsolatedEvalTarget.CreateAsync(WithFakeChat(new FakeAiProvider(_ => model)), dataset.Corpus, Ct);

        var result = await Runner().RunAsync(Plan(dataset), target, [], cancellationToken: Ct);

        var item = result.Items.Single();
        item.Metrics[EvalMetrics.CitationValidity].ShouldBe(0.5);
        item.Citations.Select(citation => (citation.Ref, citation.Valid)).ShouldBe([("S1", true), ("S9", false)]);
        item.Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(1);
        result.Settings.ChatModelLabel.ShouldBe("fake/fake-chat");
    }

    [Fact]
    public async Task An_agent_that_never_cites_fails_the_gate()
    {
        var model = new ScriptedChatClient()
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "vacation days per year" })
            .ThenText("Every employee receives 27 vacation days per calendar year.")
            .ThenText("The chief executive officer is Jane Doe.");
        var dataset = SmallDataset(VacationDays, Ceo);
        await using var target = await IsolatedEvalTarget.CreateAsync(WithFakeChat(new FakeAiProvider(_ => model)), dataset.Corpus, Ct);

        var result = await Runner().RunAsync(Plan(dataset), target, [], cancellationToken: Ct);
        var gateConfig = EvalDatasetLoader.LoadEmbedded().Single(golden => golden.Name == GoldenSetGateTests.DatasetName).Gate;
        var gate = QualityGate.Evaluate(gateConfig, result.Metrics, result.FailedItems, null, result.Items.Count, dataset.Items.Count);

        result.Metrics[EvalMetrics.AnsweredWithCitation].Mean.ShouldBe(0);
        result.Metrics[EvalMetrics.AnswerAbstained].Mean.ShouldBe(0, "an answer without citations is not a decline");
        result.Metrics.ShouldNotContainKey(EvalMetrics.CitationValidity);
        gate.Passed.ShouldBeFalse();
        gate.Checks.Where(check => !check.Passed).Select(check => check.Metric)
            .ShouldBe([EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationValidity, EvalMetrics.AnswerAbstained], ignoreOrder: true);
    }

    [Fact]
    public async Task Runs_record_the_search_mode_and_rerank_through_the_configured_reranker()
    {
        var model = new ScriptedChatClient()
            .ThenToolCall(ToolNames.SearchKnowledgeBase, new { query = "vacation days per year" })
            .ThenText("Every employee receives 27 vacation days per calendar year [S1].");
        var rerankModel = new ScriptedChatClient();
        for (var call = 0; call < 2; call++)
        {
            rerankModel.Then(RateVacationPassages);
        }

        var dataset = SmallDataset(VacationDays);
        var provider = new FakeAiProvider(profile => profile.Name == "rerank" ? rerankModel : model);
        await using var target = await IsolatedEvalTarget.CreateAsync(
            WithFakeChat(provider, new Dictionary<string, string?>
            {
                ["Search:Mode"] = "keyword",
                ["Search:Rerank"] = "true",
                ["AI:Profiles:rerank:Provider"] = "fake",
                ["AI:Profiles:rerank:Model"] = "fake-rerank",
            }),
            dataset.Corpus,
            Ct);

        var result = await Runner().RunAsync(Plan(dataset), target, [], cancellationToken: Ct);

        result.Settings.SearchMode.ShouldBe(SearchModes.Keyword);
        result.Settings.RerankModel.ShouldBe("fake/fake-rerank");
        EvalSignature.Create(result.Settings, judge: false).ShouldEndWith("__rerank-fake-fake-rerank");
        rerankModel.Requests.Count.ShouldBe(2, "the retrieval check and the agent's search are both reranked");
        rerankModel.RemainingSteps.ShouldBe(0);
        var item = result.Items.Single();
        item.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
        item.Retrieved.First().Expected.ShouldBeTrue();
        item.Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(1);

        static ChatResponse RateVacationPassages(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
        {
            var ratings = Regex.Matches(messages[^1].Text, @"^\[(\d+)\] (.*)$", RegexOptions.Multiline)
                .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Contains("Vacation", StringComparison.Ordinal) ? 3 : 0);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(ratings)));
        }
    }

    [Fact]
    public async Task The_judge_grades_answers_against_the_full_passages_in_its_own_trace()
    {
        var judgeModel = new ScriptedChatClient().ThenText(Verdicts);
        var provider = new FakeAiProvider(profile => profile.Name == EvalJudge.DefaultProfile ? judgeModel : new RuleBasedChatClient());
        var dataset = SmallDataset(VacationDays);
        await using var target = await IsolatedEvalTarget.CreateAsync(WithFakeChat(provider), dataset.Corpus, Ct);

        var result = await Runner().RunAsync(Plan(dataset, judge: true), target, [], cancellationToken: Ct);

        result.Settings.JudgeModel.ShouldBe("fake/" + EvalTestHost.JudgeModel);
        var item = result.Items.Single();
        item.Metrics[EvalMetrics.JudgeFaithfulness].ShouldBe(1);
        item.Metrics[EvalMetrics.JudgeCorrectness].ShouldBe(0.5);
        item.Judgement.ShouldNotBeNull().Criteria.Count.ShouldBe(3);

        // The judge sees the stored chunk text, not the 300-character citation snippet.
        var prompt = judgeModel.Requests.Single().Messages.Single().Text;
        prompt.ShouldContain("Every employee receives 27 vacation days per calendar year.");
        prompt.ShouldContain("department head at least six weeks in advance");
    }

    [Fact]
    public async Task Requesting_the_judge_without_an_llm_stops_the_run_before_any_item()
    {
        var dataset = SmallDataset(VacationDays);
        await using var target = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);
        var observer = new RecordingObserver();

        var error = await Should.ThrowAsync<EvalRunException>(() => Runner().RunAsync(Plan(dataset, judge: true), target, [observer], cancellationToken: Ct));

        error.Message.ShouldContain("'judge' profile");
        observer.Started.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelling_returns_the_items_finished_so_far()
    {
        var dataset = SmallDataset(VacationDays, GripperError, Ceo);
        await using var target = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var observer = new RecordingObserver(onItem: () => cancellation.Cancel());

        var result = await Runner().RunAsync(Plan(dataset, parallelism: 1), target, [observer], cancellationToken: cancellation.Token);

        result.Cancelled.ShouldBeTrue();
        result.Items.Count.ShouldBe(1);
        observer.Completed.ShouldBe(1);
    }

    [Fact]
    public async Task Experiment_runs_tag_each_turn_trace_and_pass_the_root_span_to_observers()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith(KnowledgeAgentTelemetry.Prefix, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var experimentId = Guid.NewGuid().ToString("D");
        var dataset = SmallDataset(VacationDays);
        await using var target = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);
        var observer = new RecordingObserver();
        var experiment = new EvalExperimentIdentity(experimentId, "tagged", "ds-1", null, "evaluation", new Dictionary<string, string> { ["source"] = "test" });

        var result = await Runner().RunAsync(Plan(dataset) with { Experiment = experiment }, target, [observer], cancellationToken: Ct);

        var item = result.Items.Single();
        var outcome = observer.Items.Single();
        item.TraceId.ShouldNotBeNull();
        outcome.RootSpanId.ShouldNotBeNull();

        var spans = stopped.Where(activity => Equals(activity.GetTagItem(ExperimentItemScope.ExperimentId), experimentId)).ToList();
        var root = spans.Single(activity => activity.OperationName == KnowledgeAgentTelemetry.ChatTurnSpanName);
        root.TraceId.ToHexString().ShouldBe(item.TraceId);
        root.SpanId.ToHexString().ShouldBe(outcome.RootSpanId);
        root.GetTagItem(ExperimentItemScope.ItemId).ShouldBe("small-golden:vacation-days");
        root.GetTagItem(ExperimentItemScope.ExperimentMetadataPrefix + "chat_model").ShouldBe("fake/fake-chat");
        root.GetTagItem(ExperimentItemScope.ItemExpectedOutput).ShouldNotBeNull().ToString()!.ShouldContain("27 vacation days");
        spans.Count.ShouldBeGreaterThan(1);
        spans.ShouldAllBe(activity => activity.TraceId == root.TraceId && Equals(activity.GetTagItem(ExperimentItemScope.ItemRootObservationId), outcome.RootSpanId));
    }

    [Fact]
    public async Task Live_targets_match_sources_exactly_and_delete_the_conversations_they_create()
    {
        var dataset = SmallDataset(VacationDays);
        await using var knowledgeBase = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);
        var cafeteria = knowledgeBase.Sources["cafeteria.md"];
        var live = new LiveEvalTarget(knowledgeBase.Services);

        var resolved = await live.ResolveSourcesAsync(["vacation policy", "robot-errors.md", cafeteria.Id.ToString(), "Vacation", "nope.md"], Ct);

        resolved["vacation policy"].ShouldBe([knowledgeBase.Sources["vacation.md"].Id]);
        resolved["robot-errors.md"].ShouldBe([knowledgeBase.Sources["robot-errors.md"].Id]);
        resolved[cafeteria.Id.ToString()].ShouldBe([cafeteria.Id]);
        resolved["Vacation"].ShouldBeEmpty();
        resolved.Unresolved.ShouldBe(["Vacation", "nope.md"], ignoreOrder: true);

        var liveDataset = SmallDataset(VacationDays with { ExpectedSources = ["Vacation policy"] });
        var result = await Runner().RunAsync(Plan(liveDataset), live, [], cancellationToken: Ct);

        result.Settings.Target.ShouldBe(EvalTargets.Live);
        result.Items.Single().Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
        await using var scope = knowledgeBase.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().Conversations.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_multi_turn_question_replays_the_earlier_visitor_turns_in_one_conversation_and_deletes_it()
    {
        await using var knowledgeBase = await IsolatedEvalTarget.CreateAsync(TestModels(), SmallDataset(VacationDays).Corpus, Ct);
        var live = new LiveEvalTarget(knowledgeBase.Services);
        var multiTurn = VacationDays with
        {
            History = [new EvalTurn(EvalTurnRoles.User, "I work full time."), new EvalTurn(EvalTurnRoles.Assistant, "Thanks, how can I help?")],
        };

        var result = await Runner().RunAsync(Plan(SmallDataset(multiTurn)), live, [], cancellationToken: Ct);

        var item = result.Items.Single();
        item.Status.ShouldBe(EvalItemStatuses.Completed);
        item.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(1);
        await using var scope = knowledgeBase.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().Conversations.AnyAsync(Ct))
            .ShouldBeFalse("the replayed turns and the graded one share a conversation, which the run deletes");
    }

    [Fact]
    public async Task Expected_sources_missing_from_the_knowledge_base_become_warnings()
    {
        var dataset = SmallDataset(VacationDays);
        await using var knowledgeBase = await IsolatedEvalTarget.CreateAsync(TestModels(), dataset.Corpus, Ct);

        var result = await Runner().RunAsync(Plan(SmallDataset(VacationDays with { ExpectedSources = ["Holiday calendar"] })), new LiveEvalTarget(knowledgeBase.Services), [], cancellationToken: Ct);

        result.Warnings.ShouldContain("Expected source 'Holiday calendar' is not in the knowledge base (items: vacation-days).");
        result.Items.Single().Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(0);
    }

    private static EvalRunner Runner() => new(NullLogger.Instance);

    private static EvalRunPlan Plan(EvalDataset dataset, bool judge = false, int parallelism = 1) =>
        new(Guid.NewGuid(), "test run", dataset, judge, null, parallelism, 10);

    private sealed class RecordingObserver(Action? onItem = null) : IEvalRunObserver
    {
        public int Started;
        public int Completed;
        public ConcurrentQueue<EvalItemOutcome> Items { get; } = new();

        public Task OnRunStartedAsync(EvalRunPlan plan, EvalRunSettings settings, int totalItems, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Started);
            return Task.CompletedTask;
        }

        public Task OnItemCompletedAsync(EvalRunPlan plan, EvalItemOutcome outcome, CancellationToken cancellationToken)
        {
            Items.Enqueue(outcome);
            onItem?.Invoke();
            return Task.CompletedTask;
        }

        public Task OnRunCompletedAsync(EvalRunResult result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Completed);
            return Task.CompletedTask;
        }
    }
}
