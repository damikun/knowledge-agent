using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Judge;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Evaluations.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class JudgeTests
{
    private const string Verdicts = """
        {"faithfulness":{"reasoning":"Every claim is in S1.","verdict":"yes","issues":[]},
         "relevance":{"reasoning":"Mostly on topic.","verdict":"partially","issues":["padding"]},
         "correctness":{"reasoning":"Says 25 instead of 27.","verdict":"no","issues":["wrong number of days"]}}
        """;

    private static readonly EvalItemFixture Item = new();

    [Fact]
    public async Task Renders_the_judge_prompt_and_maps_verdicts_to_scores()
    {
        var judgeModel = new ScriptedChatClient().ThenText("Sure, here it is:\n```json\n" + Verdicts + "\n```");
        await using var target = await CreateTargetAsync(judgeModel);

        var outcome = await Judge(target).JudgeAsync(
            Item.Value,
            "Employees get 25 days [S1].",
            [new JudgePassage("S1", "Vacation policy", "Every employee receives 27 vacation days per calendar year.")],
            new Dictionary<string, string>(),
            TestContext.Current.CancellationToken);

        outcome.Judgement.Error.ShouldBeNull();
        outcome.Judgement.Model.ShouldBe("fake/" + EvalTestHost.JudgeModel);
        outcome.Metrics[EvalMetrics.JudgeFaithfulness].ShouldBe(1);
        outcome.Metrics[EvalMetrics.JudgeRelevance].ShouldBe(0.5);
        outcome.Metrics[EvalMetrics.JudgeCorrectness].ShouldBe(0);
        outcome.Judgement.Criteria.Single(criterion => criterion.Name == "correctness").Issues.ShouldBe(["wrong number of days"]);

        var prompt = judgeModel.Requests.Single().Messages.Single().Text;
        prompt.ShouldContain("How many vacation days do employees get?");
        prompt.ShouldContain("[S1] Vacation policy");
        prompt.ShouldContain("Every employee receives 27 vacation days");
        prompt.ShouldContain("Employees get 25 days [S1].");
        prompt.ShouldContain("Reference answer: 27 vacation days per year.");
        prompt.ShouldContain("\"27 days\"");
    }

    [Fact]
    public async Task Asks_again_once_when_the_reply_is_not_the_json_object()
    {
        var judgeModel = new ScriptedChatClient().ThenText("I think it is fine.").ThenText(Verdicts);
        await using var target = await CreateTargetAsync(judgeModel);

        var outcome = await Judge(target).JudgeAsync(Item.Value, "Answer [S1].", [], new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        outcome.Metrics.Count.ShouldBe(3);
        judgeModel.Requests.Count.ShouldBe(2);
        judgeModel.Requests[1].Messages.Last().Text.ShouldContain("only the JSON object");
    }

    [Fact]
    public async Task Reports_an_error_without_scores_when_the_reply_stays_unreadable()
    {
        var judgeModel = new ScriptedChatClient().ThenText("nope").ThenText("{\"faithfulness\":{\"verdict\":\"maybe\"}}");
        await using var target = await CreateTargetAsync(judgeModel);

        var outcome = await Judge(target).JudgeAsync(Item.Value, "Answer.", [], new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        outcome.Metrics.ShouldBeEmpty();
        outcome.Judgement.Error.ShouldNotBeNull().ShouldContain("could not be read");
    }

    [Fact]
    public async Task Is_unavailable_when_the_judge_profile_has_no_model()
    {
        await using var target = await IsolatedEvalTarget.CreateAsync(EvalTestHost.TestModels(), [], TestContext.Current.CancellationToken);

        var (model, unavailable) = await Judge(target).DescribeAsync(TestContext.Current.CancellationToken);

        model.ShouldBeNull();
        unavailable.ShouldNotBeNull().ShouldContain("'judge' profile");
    }

    [Theory]
    [InlineData("""{"faithfulness":{"verdict":"PASS"},"relevance":{"verdict":"partial"},"correctness":{"verdict":"False"}}""", "yes", "partially", "no")]
    [InlineData("noise {\"faithfulness\":{\"verdict\":\"yes\"},\"relevance\":{\"verdict\":\"yes\"},\"correctness\":{\"verdict\":\"yes\"}} noise", "yes", "yes", "yes")]
    public void The_parser_tolerates_synonyms_and_surrounding_text(string reply, string faithfulness, string relevance, string correctness)
    {
        JudgeResponseParser.TryParse(reply, out var criteria, out var problem).ShouldBeTrue(problem);
        criteria.Select(criterion => criterion.Verdict).ShouldBe([faithfulness, relevance, correctness]);
    }

    [Theory]
    [InlineData("", "no JSON object")]
    [InlineData("{\"faithfulness\":{\"verdict\":\"yes\"}}", "\"relevance\" is missing")]
    [InlineData("{ not json }", "invalid JSON")]
    public void The_parser_explains_what_is_wrong(string reply, string expected)
    {
        JudgeResponseParser.TryParse(reply, out _, out var problem).ShouldBeFalse();
        problem.ShouldNotBeNull().ShouldContain(expected);
    }

    private static async Task<IsolatedEvalTarget> CreateTargetAsync(ScriptedChatClient judgeModel)
    {
        var provider = new FakeAiProvider(profile => profile.Name == EvalJudge.DefaultProfile ? judgeModel : new RuleBasedChatClient());
        return await IsolatedEvalTarget.CreateAsync(EvalTestHost.WithFakeChat(provider), [], TestContext.Current.CancellationToken);
    }

    private static EvalJudge Judge(IEvalTarget target) => new(
        target.Services.GetRequiredService<IPromptService>(),
        target.Services.GetRequiredService<IChatClientFactory>(),
        EvalJudge.DefaultProfile,
        EvalRunner.DefaultEnvironment,
        NullLogger.Instance);

    private sealed class EvalItemFixture
    {
        public EvalItem Value { get; } = EvalTestHost.Item(
            "days",
            "How many vacation days do employees get?",
            sources: ["vacation.md"],
            facts: ["27 days|twenty-seven days"],
            reference: "27 vacation days per year.");
    }
}
