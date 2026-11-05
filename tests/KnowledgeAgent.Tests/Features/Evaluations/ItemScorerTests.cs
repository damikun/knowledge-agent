using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Search;
using static KnowledgeAgent.Tests.Features.Evaluations.Support.EvalTestHost;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class ItemScorerTests
{
    private static readonly Guid Handbook = Guid.NewGuid();
    private static readonly Guid Manual = Guid.NewGuid();
    private static readonly Guid Unrelated = Guid.NewGuid();

    private static readonly ResolvedSources Sources = new(new Dictionary<string, IReadOnlySet<Guid>>(StringComparer.OrdinalIgnoreCase)
    {
        ["handbook.md"] = new HashSet<Guid> { Handbook },
        ["manual.md"] = new HashSet<Guid> { Manual },
        ["missing.md"] = new HashSet<Guid>(),
    });

    [Fact]
    public void Retrieval_metrics_rank_chunks_per_expected_source()
    {
        var item = Item("q", "question", sources: ["handbook.md", "manual.md"]);
        var retrieval = Hits(Unrelated, Unrelated, Handbook, Unrelated, Unrelated, Unrelated, Manual);

        var score = ItemScorer.Score(item, retrieval, Turn(""), Sources);

        score.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(0.5);
        score.Metrics[EvalMetrics.RetrievalRecallAt10].ShouldBe(1);
        score.Metrics[EvalMetrics.RetrievalMrrAt10].ShouldBe(1.0 / 3, 1e-9);
        score.Retrieved.Select(source => (source.Rank, source.Expected)).ShouldBe([(1, false), (3, true), (7, true)]);
    }

    [Fact]
    public void Unresolved_expected_sources_count_as_not_found()
    {
        var item = Item("q", "question", sources: ["handbook.md", "missing.md"]);

        var score = ItemScorer.Score(item, Hits(Handbook), Turn("Answer [S1].", Cite("S1", Handbook)), Sources);

        score.Metrics[EvalMetrics.RetrievalRecallAt5].ShouldBe(0.5);
        score.Metrics[EvalMetrics.CitationSourceRecall].ShouldBe(0.5);
    }

    [Fact]
    public void Citation_metrics_compare_answer_tags_with_the_passages_the_agent_received()
    {
        var item = Item("q", "question", sources: ["handbook.md", "manual.md"], facts: ["27 days", "Q-417"]);
        var turn = Turn(
            "Employees get 27 days of leave [S1]. The gripper code is explained in [S2]. Coffee is free [S3]. Made up [S9].",
            Cite("S1", Handbook),
            Cite("S2", Manual),
            Cite("S3", Unrelated),
            Cite("S4", Handbook));

        var score = ItemScorer.Score(item, Hits(Handbook), turn, Sources);

        score.Metrics[EvalMetrics.CitationValidity].ShouldBe(0.75);
        score.Metrics[EvalMetrics.AgentSourceRecall].ShouldBe(1);
        score.Metrics[EvalMetrics.CitationSourceRecall].ShouldBe(1);
        score.Metrics[EvalMetrics.CitationPrecision].ShouldBe(2.0 / 3, 1e-9);
        score.Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(0.5);
        score.MissingFacts.ShouldBe(["Q-417"]);
        score.Citations.Single(citation => citation.Ref == "S9").Valid.ShouldBeFalse();
        score.Metrics.ShouldNotContainKey(EvalMetrics.AnswerAbstained);
    }

    [Fact]
    public void Unanswerable_items_measure_abstention_and_skip_source_metrics()
    {
        var item = Item("q", "Who is the CEO?", answerable: false);

        var declined = ItemScorer.Score(item, Hits(Handbook), Turn("The knowledge base does not say who the CEO is."), Sources);
        var answered = ItemScorer.Score(item, Hits(Handbook), Turn("It is Jane Doe [S1].", Cite("S1", Handbook)), Sources);

        declined.Metrics[EvalMetrics.AnswerAbstained].ShouldBe(1);
        answered.Metrics[EvalMetrics.AnswerAbstained].ShouldBe(0);
        declined.Metrics.ShouldNotContainKey(EvalMetrics.RetrievalRecallAt5);
        declined.Metrics.ShouldNotContainKey(EvalMetrics.CitationSourceRecall);
        declined.Metrics.ShouldNotContainKey(EvalMetrics.CitationCoverage);
    }

    [Fact]
    public void A_failed_turn_scores_its_empty_answer_but_never_counts_as_declining()
    {
        var unanswerable = Item("u", "Who is the CEO?", answerable: false);
        var answerable = Item("a", "How many days?", sources: ["handbook.md"], facts: ["27 days"]);
        var failed = Turn(string.Empty) with { Error = "provider error" };

        ItemScorer.Score(unanswerable, Hits(), failed, Sources).Metrics.ShouldNotContainKey(EvalMetrics.AnswerAbstained);
        var score = ItemScorer.Score(answerable, Hits(Handbook), failed, Sources);
        score.Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(0);
        score.Metrics[EvalMetrics.CitationSourceRecall].ShouldBe(0);
    }

    [Fact]
    public void A_failed_retrieval_check_leaves_retrieval_metrics_out()
    {
        var item = Item("q", "question", sources: ["handbook.md"]);

        var score = ItemScorer.Score(item, new RetrievalResult([], "embedding provider down"), Turn("x"), Sources);

        score.Metrics.ShouldNotContainKey(EvalMetrics.RetrievalRecallAt5);
        score.Metrics.ShouldContainKey(EvalMetrics.AgentSourceRecall);
    }

    private static RetrievalResult Hits(params Guid[] sources) =>
        new(sources.Select((source, index) => new KnowledgeSearchHit(Guid.NewGuid(), source, index, "Title " + index, null, null, "content", 1 - index / 100.0)).ToList(), null);

    private static CitationDto Cite(string reference, Guid source) =>
        new(reference, source, Guid.NewGuid(), "Title of " + reference, null, null, "snippet", 0.9);

    private static AgentTurnResult Turn(string answer, params CitationDto[] available) => new(
        answer,
        available.ToDictionary(citation => citation.Ref, StringComparer.OrdinalIgnoreCase),
        ChatFinishReasons.Stop,
        null,
        null,
        "fake",
        "fake-model",
        10,
        20,
        1,
        Guid.NewGuid(),
        null,
        null,
        5);
}
