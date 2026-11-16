import type { Guid, IsoDateTime } from '../../lib/types';
import type { SearchMode } from '../sources/types';

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

type EvalDatasetSource = 'builtin' | 'langfuse' | 'custom';

export interface EvalDatasetDto {
  name: string;
  source: EvalDatasetSource;
  description: string | null;
  itemCount: number;
  corpusDocuments: number;
  tags: string[];
  langfuseDatasetId: string | null;
  langfuseUrl: string | null;
  id?: string | null;
  copiedFrom?: string | null;
  updatedAt?: string | null;
}

export interface EvalDatasetItemDto {
  id: string;
  question: string;
  referenceAnswer: string | null;
  expectedFacts: string[];
  expectedSources: string[];
  answerable: boolean;
  tags: string[];
  conversationId: string | null;
  messageId: string | null;
  createdAt: string;
  updatedAt: string;
  source?: EvalItemSource;
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
  source?: EvalItemSource | null;
  history?: EvalTurn[];
}

export interface ImportEvalItemsResultDto {
  created: number;
  skipped: number;
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
  langfuseConfigured: boolean;
  langfuseError: string | null;
  judgeAvailable: boolean;
  judgeModel: string | null;
}

export interface EvalDatasetSyncResultDto {
  dataset: string;
  langfuseDatasetId: string;
  created: number;
  updated: number;
  unchanged: number;
  archived: number;
  langfuseUrl: string | null;
}

export interface StartEvalRunRequest {
  dataset: string;
  source?: EvalDatasetSource;
  judge?: boolean;
  profile?: string | null;
  maxItems?: number | null;
  tags?: string[] | null;
  name?: string | null;
}

export interface EvalRunListParams {
  limit?: number | null;
  dataset?: string | null;
}

export interface EvalMetricValueDto {
  mean: number;
  count: number;
}

export type EvalGateCheckKind = 'minimum' | 'maximum' | 'regression' | 'required';

export interface EvalGateCheckDto {
  metric: string;
  kind: EvalGateCheckKind;
  actual: number | null;
  expected: number;
  passed: boolean;
  message: string;
}

export interface EvalGateDto {
  passed: boolean;
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
  failedItems: number;
  chatModel: string | null;
  embeddingModel: string | null;
  metrics: Record<string, EvalMetricValueDto>;
  gate: EvalGateDto | null;
  error: string | null;
  langfuseUrl: string | null;
  createdAt: IsoDateTime;
  startedAt: IsoDateTime | null;
  completedAt: IsoDateTime | null;
}

type EvalRunTarget = 'isolated' | 'live';

export interface EvalRunConfigDto {
  target: EvalRunTarget;
  profile: string;
  chatModel: string | null;
  embeddingModel: string | null;
  judgeModel: string | null;
  promptName: string | null;
  promptVersion: number | null;
  promptOrigin: string | null;
  parallelism: number;
  topK: number;
  maxItems: number | null;
  tags: string[];
  searchMode?: SearchMode | null;
  rerankModel?: string | null;
}

export interface EvalTagSummaryDto {
  tag: string;
  items: number;
  metrics: Record<string, EvalMetricValueDto>;
}

interface EvalRetrievedSourceDto {
  rank: number;
  title: string;
  score: number;
  expected: boolean;
}

export interface EvalCitationResultDto {
  ref: string;
  valid: boolean;
  title: string | null;
  expected: boolean;
}

export interface EvalCriterionDto {
  name: string;
  verdict: string;
  score: number;
  reasoning: string;
  issues: string[];
}

export interface EvalJudgementDto {
  model: string | null;
  criteria: EvalCriterionDto[];
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
  status: 'completed' | 'failed';
  answer: string | null;
  finishReason: string | null;
  error: string | null;
  metrics: Record<string, number>;
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
  warnings: string[];
}
