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
| Tests | `GoldenSetGateTests` (part of `dotnet test`) | retrieval and citation plumbing, on test embeddings and a rule-based agent, in memory and on Postgres + ClickHouse |

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
| `answered_with_citation` | 1 when the answer has at least one valid `[S#]` tag, else 0 (a failed turn has none) | answerable items |
| `citation_validity` | share of the answer's `[S#]` tags that point at passages the agent actually received | answers with citations |
| `citation_source_recall` | share of expected sources the answer cites | answerable items |
| `citation_precision` | share of cited sources that are expected | answers with citations |
| `citation_coverage` | share of substantive paragraphs/list items carrying a citation | answerable items |
| `answer_fact_recall` | share of expected facts in the answer (case-insensitive, normalized, on word boundaries, any alternative) | items with facts |
| `answer_abstained` | 1 when the answer says the knowledge base does not answer the question; with `--judge` also `judge_correctness` ≥ 0.5 | unanswerable items |
| `judge_faithfulness` | LLM judge: every claim is supported by the passages, and every citation by its passage | with `--judge` |
| `judge_relevance` | LLM judge: the answer addresses the question | with `--judge` |
| `judge_correctness` | LLM judge: agrees with the reference answer (or correctly declines) | with `--judge` |

The retrieval metrics run the question as a plain search, untraced, so they measure the search on its own. The
agent metrics measure what the agent did with its own queries. A gap between the two shows whether a problem is in
retrieval or in how the model searches. When part of that search fails and is skipped (keyword search, reranking,
the source lookup), the run gets a warning, so low retrieval scores from an outage are not mistaken for a regression.

`citation_validity` only counts answers that cite, so on its own it rewards an agent that never cites.
`answered_with_citation` closes that gap. `answer_abstained` looks for a stated decline ("I couldn't find…",
"the knowledge base doesn't mention…", "has no information on…", "not in the knowledge base", "no source…"), not for
missing citations: a decline may cite the closest passages, and an answer without citations may be a guess.

### The LLM judge

The judge is the `eval-judge` prompt (seeded into Langfuse like the other prompts, editable there, local default in
`src/KnowledgeAgent/Features/Prompts/Defaults/eval-judge.hbs`) running on the `judge` model profile (Models page). For each
answer it receives the question, the full text of every passage the agent received (up to 8,000 characters per
passage and 80,000 in all, so a whole chunk always fits), the answer, the reference answer and the expected facts. It returns `yes` / `partially` / `no` (scored 1 / 0.5 / 0) with reasoning and issues
for each criterion. A three-step verdict per criterion is more stable across runs than a fine-grained score. Each
judge call is its own Langfuse trace (`eval-judge`, environment `evaluation`), separate from the item it grades.
The judge needs a chat model. When the `judge` profile resolves to none, the portal disables the option and the CLI
reports the judge as unavailable.

## Quality gate and baselines

```json
"gate": {
  "required": ["citation_validity", "answered_with_citation", "answer_fact_recall", "answer_abstained", "citation_coverage"],
  "requiredWithJudge": ["judge_faithfulness", "judge_correctness"],
  "minimums": { "citation_validity": 0.99, "answered_with_citation": 0.9, "answer_abstained": 0.75, "judge_faithfulness": 0.9 },
  "maxRegression": 0.05,
  "tolerances": { "answer_abstained": 0.15, "judge_correctness": 0.08 },
  "offline": { "minimums": { "retrieval_recall_at_5": 0.85, "answered_with_citation": 1.0, "citation_validity": 1.0 } }
}
```

A run passes the gate when:

1. no item failed (agent errors make the scores incomplete),
2. every metric in `gate.required` was measured, and with `--judge` every metric in `gate.requiredWithJudge` too.
   A required metric that is missing fails the gate, because "could not be measured" must not read as "fine". A run
   over a subset (`--max-items`, `--tag`) only notes it, since the subset may hold no item the metric applies to,
3. every `gate.minimums` entry is met (default without a `gate` block: `citation_validity` ≥ 0.99). A minimum of a
   metric that is neither measured nor required is skipped, which is how judge minimums stay out of runs without
   the judge, and
4. no metric is more than its allowed drop below the baseline of the same configuration: `gate.tolerances[metric]`
   when set, else `gate.maxRegression` (default 0.05). Small sets need wider tolerances for metrics over few items:
   the golden set has 4 unanswerable questions, so one answer moves `answer_abstained` by 25 points.

`gate.offline.minimums` is for `GoldenSetGateTests` only: its deterministic test embeddings and rule-based agent
score differently from real models, so it has its own floor.

Scores are only comparable within one configuration. The signature combines the embedding model, the agent model,
the reranker when search results are reranked and, when the judge runs, the judge model, for example
`voyage-voyage-4-1024__anthropic-claude-opus-5-5` or `openai-text-embedding-3-small-1536__anthropic-claude-opus-5-5__rerank-voyage-rerank-2.5`.
Without a baseline for the signature, only 1 to 3 apply.

Answer and citation scores need a real model, so they are measured with `make eval` or the manual CI job, not on
every push. What runs on every push is `GoldenSetGateTests`: it ingests the corpus with deterministic test embeddings,
answers with a rule-based agent that cites the first passage it gets, and checks `gate.offline.minimums` (retrieval
recall, the expected sources found, `answered_with_citation` and `citation_validity`), so regressions in chunking,
retrieval, fusion and the citation plumbing still fail CI at no cost.

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
  "expectedFacts": ["27 days|27 paid vacation days|27 vacation days"],
  "referenceAnswer": "From the first day of your fifth year of service you get 27 days of paid vacation per year.",
  "tags": ["single-source", "hr"]
}
```

- Ask like a user would, not in the document's words. Paraphrases are what separate good retrieval from keyword
  matching.
- Keep expected facts short and distinctive (`"€0.32"`, `"E-214"`, `"3.3.1"`), and give alternatives with `|`
  for the ways an answer may phrase them (`"16 weeks|sixteen weeks"`, `"27 days|27 paid vacation days"`). A fact
  counts when any alternative is in the answer. Matching ignores case, Markdown bold and thousands separators, and
  stays on word boundaries, so `"27 days"` does not match "27 paid vacation days" without the second alternative.
  The loader and a test check that at least one alternative of every fact appears in its sources.
- Unanswerable items have no sources or facts. They check that the agent says the corpus does not answer.
- Tag items by kind (`single-source`, `multi-source`, `identifier`, `paraphrase`, `unanswerable`) and by topic. Reports
  break the metrics down per tag.
