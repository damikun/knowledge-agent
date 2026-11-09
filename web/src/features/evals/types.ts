import type { Guid, IsoDateTime } from '../../lib/types';
import type { SearchMode } from '../sources/types';

/** Every metric is 0..1 (higher is better) and only defined for the items it applies to. */
export const EvalMetrics = {
  RetrievalRecallAt5: 'retrieval_recall_at_5',
  RetrievalRecallAt10: 'retrieval_recall_at_10',
  RetrievalMrrAt10: 'retrieval_mrr_at_10',
  AgentSourceRecall: 'agent_source_recall',
  AnsweredWithCitation: 'answered_with_citation',
  CitationValidity: 'citation_validity',
  CitationSourceRecall: 'citation_source_recall',
  CitationPrecision: 'citation_precision',
  CitationCoverage: 'citation_coverage',
  AnswerFactRecall: 'answer_fact_recall',
  AnswerAbstained: 'answer_abstained',
  JudgeFaithfulness: 'judge_faithfulness',
  JudgeRelevance: 'judge_relevance',
  JudgeCorrectness: 'judge_correctness',
} as const;

/**
 * builtin: shipped with the app, runs in an isolated knowledge base; custom: built by the team in the portal; langfuse:
 * managed in Langfuse. Custom and Langfuse datasets run against the live knowledge base.
 */
type EvalDatasetSource = 'builtin' | 'langfuse' | 'custom';

export interface EvalDatasetDto {
  name: string;
  source: EvalDatasetSource;
  description: string | null;
  itemCount: number;
  /** Documents ingested into the isolated knowledge base; 0 for langfuse datasets. */
  corpusDocuments: number;
  tags: string[];
  langfuseDatasetId: string | null;
  langfuseUrl: string | null;
  /** Custom datasets only. */
  id?: string | null;
  copiedFrom?: string | null;
  updatedAt?: string | null;
}

export interface EvalDatasetItemDto {
  id: string;
  question: string;
  referenceAnswer: string | null;
  /** Short facts a good answer must contain. */
  expectedFacts: string[];
  /** Titles of the sources a good answer cites. */
  expectedSources: string[];
  /** False when the agent should decline. */
  answerable: boolean;
  tags: string[];
  conversationId: string | null;
  messageId: string | null;
  createdAt: string;
  updatedAt: string;
  /** Older APIs leave it out. */
  source?: EvalItemSource;
  /** Oldest first; `question` is the last turn. */
  history?: EvalTurn[];
}

export type EvalItemSource = 'manual' | 'conversation' | 'upload' | 'copy';

export interface EvalTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface SaveEvalItemRequest {
  question: string;
  referenceAnswer?: string | null;
  expectedFacts?: string[];
  expectedSources?: string[];
  answerable?: boolean;
  tags?: string[];
  conversationId?: string | null;
  messageId?: string | null;
  /** Defaults to conversation with a conversationId, else manual. */
  source?: EvalItemSource | null;
  history?: EvalTurn[];
}

/** `POST /api/evals/datasets/{id}/items/batch`: all rows or none; row errors come back as `items[i].field`. */
export interface ImportEvalItemsResultDto {
  created: number;
  skipped: number;
  /** Rows (0-based) skipped because the dataset already had the question. */
  skippedRows: number[];
  items: EvalDatasetItemDto[];
}

export interface CreateEvalDatasetRequest {
  name: string;
  description?: string | null;
  copyFrom?: string | null;
  copyFromSource?: EvalDatasetSource | null;
}

export interface EvalDatasetListDto {
  datasets: EvalDatasetDto[];
  /** Datasets can be synced and runs are recorded as experiments. */
  langfuseConfigured: boolean;
  langfuseError: string | null;
  /** The "judge" profile resolves to a configured chat model. */
  judgeAvailable: boolean;
  /** `provider/model` */
  judgeModel: string | null;
}

export interface EvalDatasetSyncResultDto {
  dataset: string;
  langfuseDatasetId: string;
  created: number;
  updated: number;
  unchanged: number;
  /** Items that exist in Langfuse but no longer in the golden set. */
  archived: number;
  langfuseUrl: string | null;
}

export interface StartEvalRunRequest {
  dataset: string;
  /** Defaults to builtin. */
  source?: EvalDatasetSource;
  /** Also scores answers with the LLM judge (costs tokens). */
  judge?: boolean;
  /** Chat model profile for the agent; defaults to the orchestrator's. */
  profile?: string | null;
  maxItems?: number | null;
  /** Only items with at least one of these tags. */
  tags?: string[] | null;
  /** Experiment name; generated when empty. */
  name?: string | null;
}

/** Empty values are not sent. */
export interface EvalRunListParams {
  /** Default 20. */
  limit?: number | null;
  dataset?: string | null;
}

/** Mean of one metric over the items where it is defined. */
export interface EvalMetricValueDto {
  mean: number;
  count: number;
}

/** minimum and regression: actual ≥ expected; maximum: actual ≤ expected. */
export type EvalGateCheckKind = 'minimum' | 'maximum' | 'regression' | 'required';

export interface EvalGateCheckDto {
  /** A 0..1 metric, or `failed_items` (a count) for the maximum check. */
  metric: string;
  kind: EvalGateCheckKind;
  actual: number | null;
  /** Lower bound for minimum and regression checks, upper bound for maximum. */
  expected: number;
  passed: boolean;
  message: string;
}

export interface EvalGateDto {
  passed: boolean;
  /** Embedding and chat model the baseline was recorded with; null without a baseline. */
  baselineSignature: string | null;
  checks: EvalGateCheckDto[];
}

export type EvalRunStatus = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface EvalRunSummaryDto {
  id: Guid;
  name: string;
  dataset: string;
  datasetSource: EvalDatasetSource;
  status: EvalRunStatus;
  judge: boolean;
  totalItems: number;
  completedItems: number;
  /** Items whose agent turn failed with an error, not items with low scores. */
  failedItems: number;
  /** `provider/model` */
  chatModel: string | null;
  /** e.g. `openai:text-embedding-3-small:1536` */
  embeddingModel: string | null;
  /** Only metrics measured on at least one item. */
  metrics: Record<string, EvalMetricValueDto>;
  gate: EvalGateDto | null;
  error: string | null;
  langfuseUrl: string | null;
  createdAt: IsoDateTime;
  startedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
}

/** isolated: builtin corpus in a temporary knowledge base; live: the app's knowledge base. */
type EvalRunTarget = 'isolated' | 'live';

export interface EvalRunConfigDto {
  target: EvalRunTarget;
  profile: string;
  /** `provider/model` */
  chatModel: string | null;
  embeddingModel: string | null;
  /** `provider/model`; null when the judge is off. */
  judgeModel: string | null;
  promptName: string | null;
  promptVersion: number | null;
  promptOrigin: string | null;
  parallelism: number;
  topK: number;
  maxItems: number | null;
  tags: string[];
  searchMode?: SearchMode | null;
  /** `provider/model`; null when search results are not reranked. */
  rerankModel?: string | null;
}

export interface EvalTagSummaryDto {
  tag: string;
  items: number;
  metrics: Record<string, EvalMetricValueDto>;
}

interface EvalRetrievedSourceDto {
  /** 1-based rank of the source's best hit. */
  rank: number;
  title: string;
  score: number;
  expected: boolean;
}

export interface EvalCitationResultDto {
  /** Tag as written in the answer, e.g. "S3". */
  ref: string;
  /** True when the tag points at a passage the agent received. */
  valid: boolean;
  /** Null for invalid tags. */
  title: string | null;
  expected: boolean;
}

export interface EvalCriterionDto {
  /** faithfulness | relevance | correctness */
  name: string;
  /** yes | partially | no */
  verdict: string;
  /** 1, 0.5 or 0. */
  score: number;
  reasoning: string;
  issues: string[];
}

export interface EvalJudgementDto {
  /** `provider/model` */
  model: string | null;
  criteria: EvalCriterionDto[];
  /** Why the judge couldn't score the item; scores are then missing. */
  error: string | null;
}

export interface EvalItemResultDto {
  itemId: string;
  question: string;
  answerable: boolean;
  tags: string[];
  expectedSources: string[];
  expectedFacts: string[];
  referenceAnswer: string | null;
  /** failed: the agent turn errored; metrics may be missing. */
  status: 'completed' | 'failed';
  answer: string | null;
  finishReason: string | null;
  error: string | null;
  /** Metrics that do not apply to the item are absent. */
  metrics: Record<string, number>;
  /** Distinct sources of the top 10 search hits, best first. */
  retrieved: EvalRetrievedSourceDto[];
  citations: EvalCitationResultDto[];
  missingFacts: string[];
  judgement: EvalJudgementDto | null;
  durationMs: number;
  inputTokens: number | null;
  outputTokens: number | null;
  toolCalls: number;
  traceId: string | null;
  langfuseTraceUrl: string | null;
}

export interface EvalRunDto {
  summary: EvalRunSummaryDto;
  config: EvalRunConfigDto;
  byTag: EvalTagSummaryDto[];
  items: EvalItemResultDto[];
  /** Problems that affected scores without stopping the run, e.g. an expected source missing from the knowledge base. */
  warnings: string[];
}
