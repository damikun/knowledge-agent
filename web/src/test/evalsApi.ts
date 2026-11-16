import type { EvalDatasetDto, EvalDatasetListDto, EvalItemResultDto, EvalRunConfigDto, EvalRunDto, EvalRunSummaryDto } from '../features/evals/types';
import { jsonResponse, type FetchHandler } from './utils';

export function runGuid(n: number): string {
  return `0199c1e2-0000-7000-8000-${String(n).padStart(12, '0')}`;
}

export const LANGFUSE_DATASET_URL = 'http://localhost:3000/project/knowledge-agent/datasets/ds-1';

export function evalDataset(overrides: Partial<EvalDatasetDto> = {}): EvalDatasetDto {
  return {
    name: 'handbook-golden',
    source: 'builtin',
    description: 'Questions about the employee handbook.',
    itemCount: 12,
    corpusDocuments: 8,
    tags: ['benefits', 'policy', 'unanswerable'],
    langfuseDatasetId: null,
    langfuseUrl: null,
    ...overrides,
  };
}

function langfuseDataset(overrides: Partial<EvalDatasetDto> = {}): EvalDatasetDto {
  return evalDataset({
    name: 'support-tickets',
    source: 'langfuse',
    description: null,
    itemCount: 40,
    corpusDocuments: 0,
    tags: ['billing'],
    langfuseDatasetId: 'ds-1',
    langfuseUrl: LANGFUSE_DATASET_URL,
    ...overrides,
  });
}

export function evalDatasetList(overrides: Partial<EvalDatasetListDto> = {}): EvalDatasetListDto {
  return {
    datasets: [evalDataset(), langfuseDataset()],
    langfuseConfigured: true,
    langfuseError: null,
    judgeAvailable: true,
    judgeModel: 'anthropic/claude-opus-5-5',
    ...overrides,
  };
}

export function evalRunSummary(overrides: Partial<EvalRunSummaryDto> = {}): EvalRunSummaryDto {
  return {
    id: runGuid(1),
    name: 'handbook-golden 2026-10-27 10:00',
    dataset: 'handbook-golden',
    datasetSource: 'builtin',
    status: 'completed',
    judge: true,
    totalItems: 12,
    completedItems: 12,
    failedItems: 0,
    chatModel: 'anthropic/claude-opus-5-5',
    embeddingModel: 'openai:text-embedding-3-small:1536',
    metrics: {
      retrieval_recall_at_5: { mean: 0.9, count: 10 },
      citation_validity: { mean: 0.95, count: 12 },
      answer_fact_recall: { mean: 0.75, count: 10 },
      judge_faithfulness: { mean: 0.875, count: 12 },
    },
    gate: { passed: true, baselineSignature: null, checks: [] },
    error: null,
    langfuseUrl: null,
    createdAt: '2026-10-27T10:00:00Z',
    startedAt: '2026-10-27T10:00:00Z',
    completedAt: '2026-10-27T10:02:30Z',
    ...overrides,
  };
}

export function runningRunSummary(overrides: Partial<EvalRunSummaryDto> = {}): EvalRunSummaryDto {
  return evalRunSummary({ status: 'running', completedItems: 3, gate: null, completedAt: null, startedAt: new Date().toISOString(), ...overrides });
}

export function evalRunConfig(overrides: Partial<EvalRunConfigDto> = {}): EvalRunConfigDto {
  return {
    target: 'isolated',
    profile: 'orchestrator',
    chatModel: 'anthropic/claude-opus-5-5',
    embeddingModel: 'openai:text-embedding-3-small:1536',
    judgeModel: 'anthropic/claude-opus-5-5',
    promptName: 'orchestrator-system',
    promptVersion: 4,
    promptOrigin: 'langfuse',
    parallelism: 4,
    topK: 10,
    maxItems: null,
    tags: [],
    searchMode: 'hybrid',
    rerankModel: 'voyage/rerank-2.5',
    ...overrides,
  };
}

export function evalItem(overrides: Partial<EvalItemResultDto> = {}): EvalItemResultDto {
  return {
    itemId: 'q1',
    question: 'How many vacation days do new employees get?',
    answerable: true,
    tags: ['policy'],
    expectedSources: ['Employee handbook', 'Benefits 2026'],
    expectedFacts: ['25 days of vacation', 'Carry-over until March'],
    referenceAnswer: 'New employees receive 25 days of vacation per year; unused days carry over until March.',
    status: 'completed',
    answer: 'New employees get **25 days** [S1]. Managers approve requests [S2]. See also [S9].',
    finishReason: 'stop',
    error: null,
    metrics: { retrieval_recall_at_5: 1, citation_validity: 0.667, answer_fact_recall: 0.5, judge_faithfulness: 1 },
    retrieved: [
      { rank: 1, title: 'Employee handbook', score: 0.83, expected: true },
      { rank: 2, title: 'Leave policy', score: 0.71, expected: false },
      { rank: 3, title: 'Benefits 2026', score: 0.65, expected: true },
    ],
    citations: [
      { ref: 'S1', valid: true, title: 'Employee handbook', expected: true },
      { ref: 'S2', valid: true, title: 'Leave policy', expected: false },
      { ref: 'S9', valid: false, title: null, expected: false },
    ],
    missingFacts: ['Carry-over until March'],
    judgement: {
      model: 'anthropic/claude-opus-5-5',
      criteria: [
        { name: 'faithfulness', verdict: 'yes', score: 1, reasoning: 'Every claim is supported by S1 and S2.', issues: [] },
        {
          name: 'correctness',
          verdict: 'partially',
          score: 0.5,
          reasoning: 'Misses the carry-over rule.',
          issues: ['Does not mention that unused days carry over until March.'],
        },
      ],
      error: null,
    },
    durationMs: 4200,
    inputTokens: 1200,
    outputTokens: 80,
    toolCalls: 2,
    traceId: 'trace-1',
    langfuseTraceUrl: 'http://localhost:3000/project/knowledge-agent/traces/trace-1',
    ...overrides,
  };
}

export function evalRunDto(summary: Partial<EvalRunSummaryDto> = {}, overrides: Partial<Omit<EvalRunDto, 'summary'>> = {}): EvalRunDto {
  return {
    summary: evalRunSummary(summary),
    config: evalRunConfig(),
    byTag: [],
    items: [evalItem()],
    warnings: [],
    ...overrides,
  };
}

function evalRunList(runs: readonly EvalRunSummaryDto[], url: URL): EvalRunSummaryDto[] {
  const dataset = url.searchParams.get('dataset');
  const limit = Number(url.searchParams.get('limit') ?? 20);
  return runs
    .filter((run) => !dataset || run.dataset === dataset)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    .slice(0, limit);
}

export function evalRunsRoutes(getRuns: () => readonly EvalRunSummaryDto[]): Record<string, FetchHandler> {
  return {
    'GET /api/evals/runs': (url) => jsonResponse(evalRunList(getRuns(), url)),
  };
}
