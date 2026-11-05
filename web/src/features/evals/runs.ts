import { isRunActive, normalizeRunStatus } from './polling';
import type { EvalRunSummaryDto } from './types';

export const RUNS_LIMIT = 20;

export function runPath(id: string): string {
  return `/evals/runs/${encodeURIComponent(id)}`;
}

export const DATASET_SOURCE_LABELS: Record<string, { label: string; description: string }> = {
  builtin: { label: 'Built-in', description: 'Ships with the app and runs in a temporary, isolated knowledge base with its own documents' },
  langfuse: { label: 'Langfuse', description: 'Managed in Langfuse; runs against your live knowledge base' },
  custom: { label: 'Yours', description: 'Built by your team in the portal; runs against your live knowledge base' },
};

export const TARGET_LABELS: Record<string, string> = {
  isolated: 'Isolated (temporary, with the dataset’s documents)',
  live: 'Live (your knowledge base)',
};

export const SEARCH_MODE_LABELS: Record<string, string> = {
  hybrid: 'Hybrid (vector + keyword)',
  vector: 'Vector',
  keyword: 'Keyword',
};

export function isBuiltinDataset(source: string | null | undefined): boolean {
  return source?.toLowerCase() === 'builtin';
}

export function isCustomDataset(source: string | null | undefined): boolean {
  return source?.toLowerCase() === 'custom';
}

function timestamp(value: string | null | undefined): number {
  return value ? Date.parse(value) : Number.NaN;
}

type RunIdentity = Pick<EvalRunSummaryDto, 'id' | 'dataset' | 'datasetSource' | 'createdAt' | 'totalItems'>;

function sameDataset(a: Pick<EvalRunSummaryDto, 'dataset' | 'datasetSource'>, b: Pick<EvalRunSummaryDto, 'dataset' | 'datasetSource'>): boolean {
  return a.dataset === b.dataset && (a.datasetSource ?? '').toLowerCase() === (b.datasetSource ?? '').toLowerCase();
}

/**
 * Newest completed run of the same dataset before `run`. Source and question count must match too: a synced
 * builtin set keeps its name but runs on another knowledge base, and a 3-question check isn't comparable.
 */
export function previousCompletedRun<T extends EvalRunSummaryDto>(run: RunIdentity, runs: readonly T[] | undefined): T | null {
  const created = timestamp(run.createdAt);
  if (Number.isNaN(created)) {
    return null;
  }
  let best: T | null = null;
  let bestCreated = Number.NEGATIVE_INFINITY;
  for (const candidate of runs ?? []) {
    const candidateCreated = timestamp(candidate.createdAt);
    if (
      candidate.id === run.id ||
      !sameDataset(candidate, run) ||
      candidate.totalItems !== run.totalItems ||
      normalizeRunStatus(candidate.status) !== 'completed' ||
      !(candidateCreated < created)
    ) {
      continue;
    }
    if (candidateCreated > bestCreated) {
      best = candidate;
      bestCreated = candidateCreated;
    }
  }
  return best;
}

/** From start to completion, or to `now` while running; null when the run never started. */
export function runDurationMs(run: Pick<EvalRunSummaryDto, 'status' | 'startedAt' | 'completedAt'>, now: number = Date.now()): number | null {
  const start = timestamp(run.startedAt);
  if (Number.isNaN(start)) {
    return null;
  }
  const end = run.completedAt ? timestamp(run.completedAt) : isRunActive(run.status) ? now : Number.NaN;
  return Number.isNaN(end) ? null : Math.max(0, end - start);
}
