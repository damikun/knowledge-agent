using System.Text;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Metrics;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class DatasetLoaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eval-dataset-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Loads_items_corpus_gate_and_baselines_from_a_folder()
    {
        Write("corpus/handbook.md", "# Handbook\nTwenty days.");
        Write("baselines/sig.json", """{"signature":"sig","items":1,"metrics":{"answer_fact_recall":0.5}}""");
        Write("dataset.json", """
            {
              // comments and trailing commas are fine
              "name": "tiny-golden",
              "description": "Tiny.",
              "corpus": [ { "file": "corpus/handbook.md", "title": "Employee handbook", "url": "https://handbook.example" } ],
              "items": [
                { "id": "days", "question": "How many days?", "expectedSources": ["Handbook.md"], "expectedFacts": ["20 days|twenty days"], "referenceAnswer": "Twenty.", "tags": ["hr", "single-source"] },
                { "id": "ceo", "question": "Who is the CEO?", "answerable": false, "tags": ["unanswerable"] },
              ],
              "gate": {
                "required": ["answered_with_citation", "citation_validity"],
                "requiredWithJudge": ["judge_faithfulness"],
                "minimums": { "citation_validity": 1 },
                "maxRegression": 0.1,
                "tolerances": { "answer_abstained": 0.15 },
                "regressionMetrics": ["answer_fact_recall"],
                "offline": { "minimums": { "answered_with_citation": 1, "retrieval_recall_at_5": 0.85 } },
              },
            }
            """);

        var dataset = EvalDatasetLoader.LoadDirectory(_directory);

        dataset.Name.ShouldBe("tiny-golden");
        dataset.Source.ShouldBe(EvalDatasetSources.Builtin);
        dataset.DirectoryPath.ShouldBe(_directory);
        dataset.Corpus.Single().ShouldSatisfyAllConditions(
            document => document.Key.ShouldBe("handbook.md"),
            document => document.Title.ShouldBe("Employee handbook"),
            document => document.Url.ShouldBe("https://handbook.example"),
            document => Encoding.UTF8.GetString(document.Content).ShouldContain("Twenty days."));
        dataset.Items.Count.ShouldBe(2);
        dataset.Items[0].Answerable.ShouldBeTrue();
        dataset.Items[1].Answerable.ShouldBeFalse();
        dataset.Tags.ShouldBe(["hr", "single-source", "unanswerable"]);
        dataset.Gate.Minimums[EvalMetrics.CitationValidity].ShouldBe(1);
        dataset.Gate.MaxRegression.ShouldBe(0.1);
        dataset.Gate.RegressionMetrics.ShouldBe([EvalMetrics.AnswerFactRecall]);
        dataset.Gate.Required.ShouldBe([EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationValidity]);
        dataset.Gate.RequiredFor(judged: true).ShouldBe([EvalMetrics.AnsweredWithCitation, EvalMetrics.CitationValidity, EvalMetrics.JudgeFaithfulness]);
        dataset.Gate.ToleranceFor(EvalMetrics.AnswerAbstained).ShouldBe(0.15);
        dataset.Gate.ToleranceFor(EvalMetrics.AnswerFactRecall).ShouldBe(0.1);
        dataset.Gate.OfflineMinimums[EvalMetrics.AnsweredWithCitation].ShouldBe(1);
        dataset.Baselines["sig"].Metrics[EvalMetrics.AnswerFactRecall].ShouldBe(0.5);
    }

    [Fact]
    public void Reports_every_problem_at_once()
    {
        Write("../outside.md", "secret");
        Write("dataset.json", """
            {
              "name": "bad name!",
              "corpus": [ { "file": "corpus/missing.md" }, { "file": "../outside.md" } ],
              "items": [
                { "id": "a", "question": "First?", "expectedSources": ["nowhere.md"] },
                { "id": "A", "question": "" },
                { "id": "u", "question": "Unanswerable?", "answerable": false, "expectedFacts": ["x"] },
                { "id": "f", "question": "Facts?", "expectedFacts": ["a||b"] }
              ],
              "gate": {
                "minimums": { "made_up": 0.5, "citation_validity": 2 },
                "required": ["not_a_metric"],
                "tolerances": { "answer_abstained": 1.5 },
                "offline": { "minimums": { "also_made_up": 1 } }
              }
            }
            """);

        var error = Should.Throw<EvalDatasetException>(() => EvalDatasetLoader.LoadDirectory(_directory));

        error.Problems.ShouldContain(problem => problem.Contains("\"name\""));
        error.Problems.ShouldContain(problem => problem.Contains("'corpus/missing.md' does not exist"));
        error.Problems.ShouldContain(problem => problem.Contains("'../outside.md' does not exist"));
        error.Problems.ShouldContain(problem => problem.Contains("used twice"));
        error.Problems.ShouldContain(problem => problem.Contains("\"question\" is empty"));
        error.Problems.ShouldContain(problem => problem.Contains("unanswerable items cannot have"));
        error.Problems.ShouldContain(problem => problem.Contains("empty alternatives"));
        error.Problems.ShouldContain(problem => problem.Contains("unknown metric 'made_up'"));
        error.Problems.ShouldContain(problem => problem.Contains("between 0 and 1"));
        error.Problems.ShouldContain("gate.required: unknown metric 'not_a_metric'.");
        error.Problems.ShouldContain("gate.tolerances.answer_abstained must be between 0 and 1.");
        error.Problems.ShouldContain("gate.offline.minimums: unknown metric 'also_made_up'.");
    }

    [Fact]
    public void Expected_sources_must_be_corpus_files_when_the_set_has_a_corpus()
    {
        Write("corpus/a.md", "A");
        Write("dataset.json", """{"name":"x","corpus":[{"file":"corpus/a.md"}],"items":[{"id":"q","question":"Q?","expectedSources":["b.md"]}]}""");

        Should.Throw<EvalDatasetException>(() => EvalDatasetLoader.LoadDirectory(_directory))
            .Problems.ShouldContain("Item 'q': expected source 'b.md' is not a corpus file.");
    }

    [Fact]
    public void The_shipped_golden_set_is_embedded_complete_and_consistent()
    {
        var dataset = EvalDatasetLoader.LoadEmbedded().Single(candidate => candidate.Name == "orbitwell-golden");

        dataset.Corpus.Count.ShouldBe(10);
        dataset.Items.Count.ShouldBe(30);
        dataset.Items.Count(item => !item.Answerable).ShouldBe(4);
        dataset.Items.Count(item => item.Tags.Contains("multi-source")).ShouldBe(5);
        dataset.Items.Where(item => item.Answerable).ShouldAllBe(item => item.ExpectedSources.Count > 0 && item.ExpectedFacts.Count > 0);
        dataset.Gate.Required.ShouldContain(EvalMetrics.AnsweredWithCitation);
        dataset.Gate.Required.ShouldContain(EvalMetrics.AnswerAbstained);
        dataset.Gate.OfflineMinimums.ShouldNotBeEmpty();

        foreach (var document in dataset.Corpus)
        {
            dataset.Items.ShouldContain(item => item.ExpectedSources.Contains(document.Key, StringComparer.OrdinalIgnoreCase), $"no item covers {document.Key}");
        }

        foreach (var item in dataset.Items.Where(item => item.Answerable))
        {
            var text = string.Join("\n", item.ExpectedSources.Select(source =>
                Encoding.UTF8.GetString(dataset.Corpus.Single(document => document.Key.Equals(source, StringComparison.OrdinalIgnoreCase)).Content)));
            foreach (var fact in item.ExpectedFacts)
            {
                AnswerText.ContainsFact(text, fact).ShouldBeTrue($"item {item.Id}: '{fact}' is not in {string.Join(", ", item.ExpectedSources)}");
            }
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_directory, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
