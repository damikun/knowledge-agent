import type { EvalRunDto, EvalRunStatus, EvalRunSummaryDto } from './types';

export const EVAL_POLL_INTERVAL_MS = 2000;

const RUN_STATUSES: readonly EvalRunStatus[] = ['queued', 'running', 'completed', 'failed', 'cancelled'];

/** Null for unknown statuses. */
export function normalizeRunStatus(status: string | null | undefined): EvalRunStatus | null {
  const value = status?.toLowerCase();
  return RUN_STATUSES.find((known) => known === value) ?? null;
}

export function isRunActive(status: string | null | undefined): boolean {
  const normalized = normalizeRunStatus(status);
  return normalized === 'queued' || normalized === 'running';
}

export function anyRunActive(runs: readonly Pick<EvalRunSummaryDto, 'status'>[] | undefined): boolean {
  return Boolean(runs?.some((run) => isRunActive(run.status)));
}

export function evalRunsRefetchInterval(runs: readonly Pick<EvalRunSummaryDto, 'status'>[] | undefined): number | false {
  return anyRunActive(runs) ? EVAL_POLL_INTERVAL_MS : false;
}

export function evalRunRefetchInterval(run: Pick<EvalRunDto, 'summary'> | undefined): number | false {
  return run && isRunActive(run.summary.status) ? EVAL_POLL_INTERVAL_MS : false;
}
