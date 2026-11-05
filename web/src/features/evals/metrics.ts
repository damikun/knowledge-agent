import { formatNumber } from '../../lib/format';
import { EvalMetrics, type EvalGateCheckDto, type EvalMetricValueDto } from './types';

export interface EvalMetricInfo {
  name: string;
  label: string;
  /** The usual name in evaluation tools. */
  technical: string;
  description: string;
}

/** In display order, which is the API's order. */
export const EVAL_METRICS: readonly EvalMetricInfo[] = [
  {
    name: EvalMetrics.RetrievalRecallAt5,
    label: 'Search finds the right source',
    technical: 'Retrieval recall@5',
    description: 'Share of the expected sources that search finds in its top 5 results for the question.',
  },
  {
    name: EvalMetrics.RetrievalRecallAt10,
    label: 'Right source in the top 10',
    technical: 'Retrieval recall@10',
    description: 'Share of the expected sources that search finds in its top 10 results for the question.',
  },
  {
    name: EvalMetrics.RetrievalMrrAt10,
    label: 'How high the right source ranks',
    technical: 'MRR@10',
    description: '1 when the first expected source is the top result, 1/2 when it is second, and so on; 0 when it is not in the top 10.',
  },
  {
    name: EvalMetrics.AgentSourceRecall,
    label: 'Agent saw the right source',
    technical: 'Agent source recall',
    description: 'Share of the expected sources that appeared in any search result the agent received while answering.',
  },
  {
    name: EvalMetrics.CitationValidity,
    label: 'Citations point to real passages',
    technical: 'Citation validity',
    description: 'Share of the [S#] citations in the answer that refer to passages the agent actually received.',
  },
  {
    name: EvalMetrics.CitationSourceRecall,
    label: 'Cites the right source',
    technical: 'Citation source recall',
    description: 'Share of the expected sources that the answer cites.',
  },
  {
    name: EvalMetrics.CitationPrecision,
    label: 'Cited sources that are right',
    technical: 'Citation precision',
    description: 'Share of the cited sources that are expected sources.',
  },
  {
    name: EvalMetrics.CitationCoverage,
    label: 'Paragraphs with a citation',
    technical: 'Citation coverage',
    description: 'Share of the answer’s paragraphs and list items that carry at least one citation.',
  },
  {
    name: EvalMetrics.AnswerFactRecall,
    label: 'Expected facts in the answer',
    technical: 'Fact recall',
    description: 'Share of the expected facts that the answer contains.',
  },
  {
    name: EvalMetrics.AnswerAbstained,
    label: 'Declines what it cannot answer',
    technical: 'Abstention rate',
    description: 'Questions the knowledge does not cover only: the answer cites nothing instead of presenting unrelated passages as an answer.',
  },
  {
    name: EvalMetrics.JudgeFaithfulness,
    label: 'Sticks to its sources',
    technical: 'Faithfulness (LLM judge)',
    description: 'An AI judge checks that every claim in the answer is supported by the passages the agent received.',
  },
  {
    name: EvalMetrics.JudgeRelevance,
    label: 'Answers the question asked',
    technical: 'Answer relevance (LLM judge)',
    description: 'An AI judge checks that the answer addresses the question.',
  },
  {
    name: EvalMetrics.JudgeCorrectness,
    label: 'Matches the expected answer',
    technical: 'Correctness (LLM judge)',
    description: 'An AI judge checks that the answer agrees with the expected answer, or correctly declines a question it cannot answer.',
  },
];

const FAILED_ITEMS_METRIC = 'failed_items';

const COUNT_METRICS: readonly EvalMetricInfo[] = [
  {
    name: FAILED_ITEMS_METRIC,
    label: 'Questions that hit an error',
    technical: 'Failed items',
    description: 'Questions whose agent turn failed with an error (not low scores).',
  },
];

const RATIO_METRICS = new Set(EVAL_METRICS.map((metric) => metric.name));
const METRICS_BY_NAME = new Map([...EVAL_METRICS, ...COUNT_METRICS].map((metric) => [metric.name, metric] as const));
const METRIC_ORDER = EVAL_METRICS.map((metric) => metric.name);

export function metricInfo(name: string): EvalMetricInfo {
  return METRICS_BY_NAME.get(name) ?? { name, label: name, technical: name, description: '' };
}

/** Known metrics in display order, then unknown ones alphabetically, without duplicates. */
export function orderMetrics(names: Iterable<string>): string[] {
  const rank = (name: string) => {
    const index = METRIC_ORDER.indexOf(name);
    return index === -1 ? Number.MAX_SAFE_INTEGER : index;
  };
  return [...new Set(names)].sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
}

export function presentMetrics(maps: Iterable<Readonly<Record<string, unknown>> | null | undefined>): string[] {
  const names: string[] = [];
  for (const map of maps) {
    if (map) {
      names.push(...Object.keys(map).filter((name) => map[name] !== undefined && map[name] !== null));
    }
  }
  return orderMetrics(names);
}

const percentFormat = new Intl.NumberFormat('en-US', { style: 'percent', maximumFractionDigits: 1 });
const pointsFormat = new Intl.NumberFormat('en-US', { signDisplay: 'exceptZero', maximumFractionDigits: 1 });

/** 0.8333 → "83.3%"; a missing value → "–". */
export function formatPercent(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? '–' : percentFormat.format(value);
}

function roundPoints(points: number): number {
  const rounded = Math.round(points * 10) / 10;
  return Object.is(rounded, -0) ? 0 : rounded;
}

/** Percentage points: 3.25 → "+3.3 pp", -1.5 → "-1.5 pp", 0.04 → "±0 pp"; a missing value → "". */
export function formatDelta(points: number | null | undefined): string {
  if (points === null || points === undefined || !Number.isFinite(points)) {
    return '';
  }
  const rounded = roundPoints(points);
  return rounded === 0 ? '±0 pp' : `${pointsFormat.format(rounded)} pp`;
}

/** In percentage points (0.80 → 0.85 is +5); null when either run did not measure the metric. */
export function metricDelta(
  current: Readonly<Record<string, EvalMetricValueDto>> | null | undefined,
  previous: Readonly<Record<string, EvalMetricValueDto>> | null | undefined,
  name: string,
): number | null {
  const now = current?.[name]?.mean;
  const before = previous?.[name]?.mean;
  if (now === undefined || before === undefined || !Number.isFinite(now) || !Number.isFinite(before)) {
    return null;
  }
  return (now - before) * 100;
}

const CHECK_KINDS: Record<string, { label: string; bound: string }> = {
  minimum: { label: 'Lowest allowed', bound: 'needs at least' },
  regression: { label: 'No drop from baseline', bound: 'needs at least' },
  maximum: { label: 'Highest allowed', bound: 'allows at most' },
};

export function gateCheckKindLabel(kind: string): string {
  return CHECK_KINDS[kind?.toLowerCase()]?.label ?? kind;
}

export function formatGateValue(metric: string, value: number | null | undefined): string | null {
  if (RATIO_METRICS.has(metric)) {
    return formatPercent(value);
  }
  if (COUNT_METRICS.some((count) => count.name === metric)) {
    return formatNumber(value);
  }
  return null;
}

/** "72% · needs at least 80%"; null when the metric or kind is unknown. */
export function describeGateCheck(check: Pick<EvalGateCheckDto, 'metric' | 'kind' | 'actual' | 'expected'>): string | null {
  const kind = CHECK_KINDS[check.kind?.toLowerCase()];
  const actual = formatGateValue(check.metric, check.actual);
  const expected = formatGateValue(check.metric, check.expected);
  return kind && actual !== null && expected !== null ? `${actual} · ${kind.bound} ${expected}` : null;
}
