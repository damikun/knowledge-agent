import { describe, expect, it } from 'vitest';
import { evalRunDto, evalRunSummary } from '../../test/evalsApi';
import {
  describeGateCheck,
  EVAL_METRICS,
  formatDelta,
  formatGateValue,
  formatPercent,
  gateCheckKindLabel,
  metricDelta,
  metricInfo,
  orderMetrics,
  presentMetrics,
} from './metrics';
import { anyRunActive, EVAL_POLL_INTERVAL_MS, evalRunRefetchInterval, evalRunsRefetchInterval, isRunActive, normalizeRunStatus } from './polling';
import { previousCompletedRun, runDurationMs } from './runs';
import { EvalMetrics, type EvalGateCheckKind } from './types';

const value = (mean: number, count = 4) => ({ mean, count });

describe('metric catalog', () => {
  it('labels and explains every metric of the API, in the API order', () => {
    expect(EVAL_METRICS.map((metric) => metric.name)).toEqual(Object.values(EvalMetrics));
    for (const metric of EVAL_METRICS) {
      expect(metric.label).not.toBe('');
      expect(metric.description).not.toBe('');
    }
    for (const metric of EVAL_METRICS) {
      expect(metric.technical).not.toBe('');
    }
    expect(metricInfo('retrieval_recall_at_5')).toMatchObject({ label: 'Search finds the right source', technical: 'Retrieval recall@5' });
    expect(metricInfo('retrieval_mrr_at_10')).toMatchObject({ label: 'How high the right source ranks', technical: 'MRR@10' });
    expect(metricInfo('agent_source_recall').label).toBe('Agent saw the right source');
    expect(metricInfo('answer_abstained').label).toBe('Declines what it cannot answer');
    expect(metricInfo('judge_correctness').technical).toBe('Correctness (LLM judge)');
  });

  it('knows the failed-items count of the gate and shows unknown metrics by their raw name', () => {
    expect(metricInfo('failed_items')).toMatchObject({ label: 'Questions that hit an error', technical: 'Failed items' });
    expect(metricInfo('context_precision')).toEqual({ name: 'context_precision', label: 'context_precision', technical: 'context_precision', description: '' });
  });

  it('orders metrics like the API and appends unknown ones alphabetically', () => {
    expect(orderMetrics(['judge_relevance', 'zeta', 'retrieval_recall_at_5', 'alpha', 'retrieval_recall_at_5'])).toEqual([
      'retrieval_recall_at_5',
      'judge_relevance',
      'alpha',
      'zeta',
    ]);
    expect(presentMetrics([{ citation_validity: value(1) }, null, { retrieval_mrr_at_10: value(0.5), citation_validity: value(0.5) }, undefined])).toEqual([
      'retrieval_mrr_at_10',
      'citation_validity',
    ]);
    expect(presentMetrics([])).toEqual([]);
  });
});

describe('metric formatting', () => {
  it('shows 0..1 metrics as percentages with at most one decimal', () => {
    expect(formatPercent(0.8333)).toBe('83.3%');
    expect(formatPercent(0.875)).toBe('87.5%');
    expect(formatPercent(1)).toBe('100%');
    expect(formatPercent(0.5)).toBe('50%');
    expect(formatPercent(0)).toBe('0%');
    expect(formatPercent(null)).toBe('–');
    expect(formatPercent(undefined)).toBe('–');
    expect(formatPercent(Number.NaN)).toBe('–');
  });

  it('shows changes in percentage points with a sign, rounded to one decimal', () => {
    expect(formatDelta(10)).toBe('+10 pp');
    expect(formatDelta(3.25)).toBe('+3.3 pp');
    expect(formatDelta(-1.54)).toBe('-1.5 pp');
    expect(formatDelta(0)).toBe('±0 pp');
    expect(formatDelta(0.04)).toBe('±0 pp');
    expect(formatDelta(-0.04)).toBe('±0 pp');
    expect(formatDelta(null)).toBe('');
    expect(formatDelta(Number.POSITIVE_INFINITY)).toBe('');
  });
});

describe('quality gate checks', () => {
  it('describes each check in the unit of its metric', () => {
    expect(describeGateCheck({ metric: 'answer_fact_recall', kind: 'minimum', actual: 0.75, expected: 0.8 })).toBe('75% · needs at least 80%');
    expect(describeGateCheck({ metric: 'retrieval_recall_at_5', kind: 'regression', actual: null, expected: 0.78 })).toBe('– · needs at least 78%');
    expect(describeGateCheck({ metric: 'failed_items', kind: 'maximum', actual: 2, expected: 0 })).toBe('2 · allows at most 0');
    expect(formatGateValue('failed_items', 1200)).toBe('1,200');
    expect(formatGateValue('citation_validity', 0.5)).toBe('50%');
  });

  it('leaves unknown metrics and kinds to the message', () => {
    expect(formatGateValue('context_precision', 0.5)).toBeNull();
    expect(describeGateCheck({ metric: 'context_precision', kind: 'minimum', actual: 0.5, expected: 0.6 })).toBeNull();
    expect(describeGateCheck({ metric: 'answer_fact_recall', kind: 'experimental' as EvalGateCheckKind, actual: 0.5, expected: 0.6 })).toBeNull();
    expect(gateCheckKindLabel('minimum')).toBe('Lowest allowed');
    expect(gateCheckKindLabel('maximum')).toBe('Highest allowed');
    expect(gateCheckKindLabel('regression')).toBe('No drop from baseline');
    expect(gateCheckKindLabel('experimental')).toBe('experimental');
  });
});

describe('change versus the previous run', () => {
  it('is the difference of the means in percentage points', () => {
    expect(metricDelta({ m: value(0.85) }, { m: value(0.8) }, 'm')).toBeCloseTo(5);
    expect(metricDelta({ m: value(0.5) }, { m: value(0.75) }, 'm')).toBeCloseTo(-25);
    expect(metricDelta({ m: value(0.5) }, { m: value(0.5) }, 'm')).toBe(0);
    expect(metricDelta({ m: value(0.5) }, { other: value(0.5) }, 'm')).toBeNull();
    expect(metricDelta({}, { m: value(0.5) }, 'm')).toBeNull();
    expect(metricDelta({ m: value(0.5) }, null, 'm')).toBeNull();
  });

  it('compares with the newest completed run of the same dataset and source created before the run', () => {
    const current = evalRunSummary({ id: 'current', createdAt: '2026-10-27T10:00:00Z' });
    const previous = evalRunSummary({ id: 'previous', createdAt: '2026-10-26T10:00:00Z' });
    const older = evalRunSummary({ id: 'older', createdAt: '2026-10-20T10:00:00Z' });
    const runs = [
      evalRunSummary({ id: 'newer', createdAt: '2026-10-28T10:00:00Z' }),
      current,
      evalRunSummary({ id: 'failed', status: 'failed', createdAt: '2026-10-27T09:00:00Z' }),
      evalRunSummary({ id: 'cancelled', status: 'cancelled', createdAt: '2026-10-27T09:15:00Z' }),
      evalRunSummary({ id: 'running', status: 'running', createdAt: '2026-10-27T09:30:00Z' }),
      evalRunSummary({ id: 'other-dataset', dataset: 'support-tickets', createdAt: '2026-10-27T08:00:00Z' }),
      evalRunSummary({ id: 'langfuse-copy', datasetSource: 'langfuse', createdAt: '2026-10-27T08:30:00Z' }),
      older,
      previous,
    ];
    expect(previousCompletedRun(current, runs)?.id).toBe('previous');
    expect(previousCompletedRun(previous, runs)?.id).toBe('older');
    expect(previousCompletedRun(older, runs)).toBeNull();
    expect(previousCompletedRun(evalRunSummary({ id: 'next', createdAt: '2026-10-29T10:00:00Z' }), runs)?.id).toBe('newer');
    expect(previousCompletedRun(current, undefined)).toBeNull();
    expect(previousCompletedRun(current, [current])).toBeNull();
  });

  it('only compares runs over the same number of questions', () => {
    const quick = evalRunSummary({ id: 'quick', totalItems: 3, createdAt: '2026-10-27T10:00:00Z' });
    const full = evalRunSummary({ id: 'full', totalItems: 30, createdAt: '2026-10-26T10:00:00Z' });
    const earlierQuick = evalRunSummary({ id: 'earlier-quick', totalItems: 3, createdAt: '2026-10-25T10:00:00Z' });
    expect(previousCompletedRun(quick, [full])).toBeNull();
    expect(previousCompletedRun(quick, [full, earlierQuick])?.id).toBe('earlier-quick');
  });

  it('accepts statuses in any casing', () => {
    const current = evalRunSummary({ id: 'current', createdAt: '2026-10-27T10:00:00Z' });
    const previous = evalRunSummary({ id: 'previous', status: 'Completed' as never, createdAt: '2026-10-26T10:00:00Z' });
    expect(previousCompletedRun(current, [previous])?.id).toBe('previous');
  });
});

describe('runs polling decision', () => {
  it('polls every 2 s while any run is queued or running', () => {
    expect(EVAL_POLL_INTERVAL_MS).toBe(2000);
    expect(evalRunsRefetchInterval([{ status: 'completed' }, { status: 'queued' }])).toBe(2000);
    expect(evalRunsRefetchInterval([{ status: 'running' }])).toBe(2000);
    expect(evalRunRefetchInterval(evalRunDto({ status: 'queued' }))).toBe(2000);
    expect(evalRunRefetchInterval(evalRunDto({ status: 'running' }))).toBe(2000);
  });

  it('stops polling when every run has finished', () => {
    expect(evalRunsRefetchInterval([{ status: 'completed' }, { status: 'failed' }, { status: 'cancelled' }])).toBe(false);
    expect(evalRunsRefetchInterval([])).toBe(false);
    expect(evalRunsRefetchInterval(undefined)).toBe(false);
    expect(evalRunRefetchInterval(evalRunDto({ status: 'completed' }))).toBe(false);
    expect(evalRunRefetchInterval(undefined)).toBe(false);
  });

  it('reads statuses in any casing and ignores unknown ones', () => {
    expect(normalizeRunStatus('Running')).toBe('running');
    expect(normalizeRunStatus('archived')).toBeNull();
    expect(normalizeRunStatus(null)).toBeNull();
    expect(isRunActive('QUEUED')).toBe(true);
    expect(isRunActive('archived')).toBe(false);
    expect(anyRunActive([{ status: 'Cancelled' as never }])).toBe(false);
  });
});

describe('run duration', () => {
  it('measures from start to completion, or to now while the run is going', () => {
    expect(runDurationMs({ status: 'completed', startedAt: '2026-10-27T10:00:00Z', completedAt: '2026-10-27T10:02:30Z' })).toBe(150_000);
    expect(runDurationMs({ status: 'running', startedAt: '2026-10-27T10:00:00Z', completedAt: null }, Date.parse('2026-10-27T10:00:42Z'))).toBe(42_000);
    expect(runDurationMs({ status: 'queued', startedAt: null, completedAt: null })).toBeNull();
    expect(runDurationMs({ status: 'cancelled', startedAt: '2026-10-27T10:00:00Z', completedAt: null })).toBeNull();
  });
});
