# Evaluations

Golden question sets measure how well the agent finds sources, cites them and answers, so every change to code,
prompts or models can be compared with the last accepted result instead of judged by feel.

```
evals/datasets/<name>/
  dataset.json      questions, the corpus they are about, and the quality gate
  corpus/           the documents (Markdown, text or PDF)
  baselines/        accepted scores, one file per configuration (written by --update-baseline)
```

The sets are embedded into the application assembly (`src/KnowledgeAgent`), so the API, the CLI and the tests always
evaluate the version in the commit. The shipped set, `orbitwell-golden`, has 30 questions about a fictional robotics company
(10 documents). It includes single-source facts, exact identifiers (error codes, header names), questions that need
two documents, paraphrases that avoid the documents' wording, and questions the corpus cannot answer.

## Running

| Where | How | What it uses |
|---|---|---|
| Portal | **Evaluations** → a dataset → **Run** | the app's providers, keys and prompts (Langfuse) |
| CLI | `make eval` / `dotnet run --project src/KnowledgeAgent.Eval -- [options]` | `src/KnowledgeAgent.Api/appsettings.json`, the API's user secrets, environment variables |
| CI | `.github/workflows/ci.yml` | the retrieval gate on every push (tests); real model + judge when started by hand |
| Tests | `GoldenSetGateTests` (part of `dotnet test`) | retrieval only, on test embeddings, in memory and on Postgres + ClickHouse |

A builtin set never touches your knowledge base. Each run ingests the corpus into a temporary in-memory knowledge base
through the real pipeline (extraction, chunking, embeddings), runs every question through the real chat service, then
throws the knowledge base away. Langfuse datasets that you create (for example from production traces with
*Add to dataset*) run against the live knowledge base. The conversations those runs create are deleted afterwards.

```bash
make eval            # your configured models and the quality gate (costs tokens)
make eval-llm        # the same plus the LLM judge
make eval-baseline   # accept this run as the baseline of its model configuration
make eval-sync       # create/update the Langfuse datasets

dotnet run --project src/KnowledgeAgent.Eval -- --help
dotnet run --project src/KnowledgeAgent.Eval -- --tag multi-source --max-items 3 --judge
dotnet run --project src/KnowledgeAgent.Eval -- --set AI:Profiles:orchestrator:Model=claude-opus-5-5 --name "opus effort high"
```

The CLI prints progress to stderr and the Markdown report to stdout. It writes `report.md` and `report.json` to
`artifacts/evals/<time>-<dataset>/` (or `--report-dir`), and appends the report to the GitHub job summary when it
runs in Actions. Exit codes: 0 done, 1 gate failed (with `--gate`), 2 error.

## Metrics

All metrics are between 0 and 1, higher is better, and are averaged over the items where they apply.

| Metric | Meaning | Applies to |
|---|---|---|
| `retrieval_recall_at_5` / `_at_10` | share of expected sources with a passage in the top 5/10 search hits for the question | answerable items |
| `retrieval_mrr_at_10` | 1 / rank of the first hit from an expected source | answerable items |
| `agent_source_recall` | share of expected sources that appeared in any search result the agent received | answerable items |
| `citation_validity` | share of the answer's `[S#]` tags that point at passages the agent actually received | answers with citations |
| `citation_source_recall` | share of expected sources the answer cites | answerable items |
| `citation_precision` | share of cited sources that are expected | answers with citations |
| `citation_coverage` | share of substantive paragraphs/list items carrying a citation | answerable items |
| `answer_fact_recall` | share of expected facts in the answer (case-insensitive, normalized, on word boundaries) | items with facts |
| `answer_abstained` | 1 when the answer cites nothing, i.e. it did not present unrelated passages as an answer | unanswerable items |
| `judge_faithfulness` | LLM judge: every claim is supported by the passages, and every citation by its passage | with `--judge` |
| `judge_relevance` | LLM judge: the answer addresses the question | with `--judge` |
| `judge_correctness` | LLM judge: agrees with the reference answer (or correctly declines) | with `--judge` |

The retrieval metrics run the question as a plain search, untraced, so they measure the search on its own. The
agent metrics measure what the agent did with its own queries. A gap between the two shows whether a problem is in
retrieval or in how the model searches.

### The LLM judge

The judge is the `eval-judge` prompt (seeded into Langfuse like the other prompts, editable there, local default in
`src/KnowledgeAgent/Features/Prompts/Defaults/eval-judge.hbs`) running on the `judge` model profile (Models page). For each
answer it receives the question, the full text of every passage the agent received, the answer, the reference
answer and the expected facts. It returns `yes` / `partially` / `no` (scored 1 / 0.5 / 0) with reasoning and issues
for each criterion. A three-step verdict per criterion is more stable across runs than a fine-grained score. Each
judge call is its own Langfuse trace (`eval-judge`, environment `evaluation`), separate from the item it grades.
The judge needs a chat model. When the `judge` profile resolves to none, the portal disables the option and the CLI
reports the judge as unavailable.

## Quality gate and baselines

A run passes the gate when:

1. no item failed (agent errors make the scores incomplete),
2. every `gate.minimums` entry in dataset.json is met (default: `citation_validity` ≥ 0.99), and
3. no metric is more than `gate.maxRegression` (default 0.05) below the baseline of the same configuration.

Scores are only comparable within one configuration. The signature combines the embedding model, the agent model,
the reranker when search results are reranked and, when the judge runs, the judge model, for example
`voyage-voyage-4-1024__anthropic-claude-opus-5-5` or `openai-text-embedding-3-small-1536__anthropic-claude-opus-5-5__rerank-voyage-rerank-2.5`.
Without a baseline for the signature, only 1 and 2 apply.

Answer and citation scores need a real model, so they are measured with `make eval` or the manual CI job, not on
every push. What runs on every push is `GoldenSetGateTests`: it ingests the corpus with deterministic test embeddings
and checks retrieval (recall and the expected sources found) against fixed thresholds, so regressions in chunking,
retrieval and fusion still fail CI at no cost.

When a change improves the scores on purpose, or the golden set changes, accept the new numbers with
`make eval-baseline` and commit the file in `baselines/`. Each model configuration has its own baseline.

## Comparing search modes

Every run records its search mode (`Search:Mode`) and reranker (the `rerank` profile as `provider/model`, or none) in
the report, the portal and the Langfuse experiment metadata. The mode is not part of the signature, so all modes are
compared with the same baseline and a worse mode shows up as regressions in the gate checks.

```bash
dotnet run --project src/KnowledgeAgent.Eval -- --set Search:Mode=vector --name "vector only"
dotnet run --project src/KnowledgeAgent.Eval -- --set Search:Mode=keyword --name "keyword only"
dotnet run --project src/KnowledgeAgent.Eval -- --set Search:KeywordWeight=0.5 --name "vector-leaning fusion"
```

To measure a reranker, run with a Voyage key or a chat provider chosen for the `rerank` profile, and compare it with
`--set Search:Rerank=false`. Measure with your own embedding model before changing the weights: lexical and semantic
embeddings favour keyword and vector search differently, and fusing both is the robust default.

## Langfuse

When `Langfuse:*` is configured, every run is also a Langfuse **experiment** (v4: built from span attributes, not a
separate API):

- The builtin set is synced to a Langfuse dataset with the same name first (item ids `<dataset>:<item>`; only changed
  items are rewritten; removed items are archived).
- Each item's chat-turn trace carries the `langfuse.experiment.*` attributes. It is linked to its dataset item and
  version, has the expected output on its root observation, and uses the `evaluation` environment.
- Each metric becomes a score on the item's root observation, and the run means become experiment-level scores.
- Compare runs in Langfuse under the dataset's experiments, filtered by the experiment metadata (chat model,
  embedding model, prompt version, judge model).

To evaluate your own sources, build a dataset in Langfuse. You can add items from production traces with
*Add to dataset*, or create them by hand. An item's input is the question, as a string or `{"question": "..."}`. Its
expected output is the reference answer as a string, or `{"answer", "facts", "sources", "answerable"}`, where
`sources` are titles, file names, URLs or ids from your knowledge base. Run it from the portal: it uses the live
knowledge base.

## Writing questions

```json
{
  "id": "hr-vacation-days-after-four-years",
  "question": "How many days of paid vacation do I get per year once I've been with Orbitwell for more than four years?",
  "answerable": true,
  "expectedSources": ["employee-handbook.md"],
  "expectedFacts": ["27 days"],
  "referenceAnswer": "From the first day of your fifth year of service you get 27 days of paid vacation per year.",
  "tags": ["single-source", "hr"]
}
```

- Ask like a user would, not in the document's words. Paraphrases are what separate good retrieval from keyword
  matching.
- Keep expected facts short and distinctive (`"€0.32"`, `"E-214"`, `"3.3.1"`), and give alternatives with `|`
  (`"16 weeks|sixteen weeks"`). The loader and a test check that every fact really appears in its sources.
- Unanswerable items have no sources or facts. They check that the agent says the corpus does not answer.
- Tag items by kind (`single-source`, `multi-source`, `identifier`, `paraphrase`, `unanswerable`) and by topic. Reports
  break the metrics down per tag.
