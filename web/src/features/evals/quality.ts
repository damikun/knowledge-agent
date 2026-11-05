import type { Tone } from '../../components/ui/tone';
import { metricDelta } from './metrics';
import { EvalMetrics, type EvalItemResultDto, type EvalMetricValueDto } from './types';

/** A UI grouping of the metrics; the API metric keys don't change. */
export type QualityAreaId = 'finds' | 'answers' | 'cites' | 'declines';

export type Verdict = 'good' | 'needs-work' | 'poor';

export interface QualityArea {
  id: QualityAreaId;
  title: string;
  /** For table columns and filters. */
  short: string;
  meaning: string;
  improve: string;
  missedLabel: string;
  /** Best first; the first one the run measured is the headline. */
  headline: readonly string[];
  supporting: readonly string[];
  /** At or above `good` is Good, at or above `fair` is Needs work, below is Poor. */
  thresholds: { good: number; fair: number };
}

// Search misses cascade into wrong answers, so finds is a bit stricter than answers and cites.
// An invented answer costs the most trust, so declines is strictest.
export const QUALITY_AREAS: readonly QualityArea[] = [
  {
    id: 'finds',
    title: 'Finds the right information',
    short: 'Finds information',
    meaning: 'How often search brings up the source that holds the answer.',
    improve: 'If it is low, check that the sources are added and indexed, and try hybrid search or reranking.',
    missedLabel: 'Missed the source',
    headline: [EvalMetrics.RetrievalRecallAt5, EvalMetrics.AgentSourceRecall],
    supporting: [EvalMetrics.AgentSourceRecall],
    thresholds: { good: 0.85, fair: 0.6 },
  },
  {
    id: 'answers',
    title: 'Answers correctly',
    short: 'Answers correctly',
    meaning: 'How much of what a good answer must say the agent actually said.',
    improve: 'If it is low, fill gaps in the sources, sharpen the prompt, or try a stronger answering model.',
    missedLabel: 'Incomplete answer',
    // Facts first: every run measures them, with or without the judge.
    headline: [EvalMetrics.AnswerFactRecall, EvalMetrics.JudgeCorrectness],
    supporting: [EvalMetrics.JudgeCorrectness, EvalMetrics.JudgeFaithfulness],
    thresholds: { good: 0.8, fair: 0.6 },
  },
  {
    id: 'cites',
    title: 'Backs answers with sources',
    short: 'Backs with sources',
    meaning: 'How often answers cite the source the answer comes from, so people can check it.',
    improve: 'If it is low, ask for citations in the prompt and make sure search finds the right source first.',
    missedLabel: 'Weak citations',
    headline: [EvalMetrics.CitationSourceRecall, EvalMetrics.CitationValidity],
    supporting: [EvalMetrics.CitationValidity, EvalMetrics.CitationPrecision],
    thresholds: { good: 0.8, fair: 0.6 },
  },
  {
    id: 'declines',
    title: 'Says “I don’t know” when it should',
    short: 'Says I don’t know',
    meaning: 'How often the agent declines questions its knowledge does not cover, instead of guessing.',
    improve: 'If it is low, tell the agent in the prompt to say it does not know when the sources do not answer.',
    missedLabel: 'Guessed instead',
    headline: [EvalMetrics.AnswerAbstained],
    supporting: [],
    thresholds: { good: 0.9, fair: 0.7 },
  },
];

const AREAS_BY_ID = new Map(QUALITY_AREAS.map((area) => [area.id, area] as const));

export function qualityArea(id: QualityAreaId): QualityArea {
  return AREAS_BY_ID.get(id)!;
}

export const VERDICTS: Readonly<Record<Verdict, { label: string; tone: Tone }>> = {
  good: { label: 'Good', tone: 'success' },
  'needs-work': { label: 'Needs work', tone: 'warning' },
  poor: { label: 'Poor', tone: 'danger' },
};

export function verdict(area: QualityArea, value: number): Verdict {
  return value >= area.thresholds.good ? 'good' : value >= area.thresholds.fair ? 'needs-work' : 'poor';
}

export function headlineMetric(area: QualityArea, metrics: Readonly<Record<string, unknown>> | null | undefined): string | null {
  return area.headline.find((name) => metrics?.[name] !== undefined && metrics?.[name] !== null) ?? null;
}

/** In pp; smaller changes are one rounding step of noise. */
const MEANINGFUL_CHANGE_PP = 1;

export type Change = { kind: 'better' | 'worse'; points: number } | { kind: 'same' };

/** Null when there's nothing to compare with. Every metric is higher-is-better. */
export function changeOf(points: number | null): Change | null {
  if (points === null || !Number.isFinite(points)) {
    return null;
  }
  if (Math.abs(points) < MEANINGFUL_CHANGE_PP) {
    return { kind: 'same' };
  }
  return { kind: points > 0 ? 'better' : 'worse', points: Math.round(points) };
}

export interface AreaScore {
  area: QualityArea;
  metric: string;
  value: EvalMetricValueDto;
  verdict: Verdict;
  change: Change | null;
  supporting: string[];
}

/** In order; areas the run has no metric for are left out. */
export function scoreAreas(
  metrics: Readonly<Record<string, EvalMetricValueDto>> | null | undefined,
  previous?: Readonly<Record<string, EvalMetricValueDto>> | null,
): AreaScore[] {
  const scores: AreaScore[] = [];
  for (const area of QUALITY_AREAS) {
    const metric = headlineMetric(area, metrics);
    if (!metric) {
      continue;
    }
    const value = metrics![metric]!;
    scores.push({
      area,
      metric,
      value,
      verdict: verdict(area, value.mean),
      change: previous ? changeOf(metricDelta(metrics, previous, metric)) : null,
      supporting: area.supporting.filter((name) => name !== metric && metrics?.[name] !== undefined),
    });
  }
  return scores;
}

export function itemAreaMetrics(items: readonly EvalItemResultDto[]): Partial<Record<QualityAreaId, string>> {
  const result: Partial<Record<QualityAreaId, string>> = {};
  for (const area of QUALITY_AREAS) {
    const metric = area.headline.find((name) => items.some((item) => item.metrics?.[name] !== undefined && item.metrics?.[name] !== null));
    if (metric) {
      result[area.id] = metric;
    }
  }
  return result;
}

/** Below the Good line; questions the metric doesn't apply to never miss. */
export function missedArea(item: EvalItemResultDto, area: QualityArea, metric: string): boolean {
  const value = item.metrics?.[metric];
  return value !== undefined && value !== null && value < area.thresholds.good;
}

export type ItemFilter = 'all' | 'errors' | QualityAreaId;

export function isFailedItem(item: EvalItemResultDto): boolean {
  return item.status?.toLowerCase() === 'failed';
}

export function filterItems(items: readonly EvalItemResultDto[], filter: ItemFilter, metrics: Partial<Record<QualityAreaId, string>>): EvalItemResultDto[] {
  if (filter === 'all') {
    return [...items];
  }
  if (filter === 'errors') {
    return items.filter(isFailedItem);
  }
  const metric = metrics[filter];
  return metric ? items.filter((item) => missedArea(item, qualityArea(filter), metric)) : [];
}

// Correctness first; the others stand in when a run didn't measure it.
const HEADLINE_ORDER: readonly QualityAreaId[] = ['answers', 'finds', 'cites', 'declines'];

export function headlineScore(metrics: Readonly<Record<string, EvalMetricValueDto>> | null | undefined): AreaScore | null {
  const scores = scoreAreas(metrics);
  return HEADLINE_ORDER.map((id) => scores.find((score) => score.area.id === id)).find((score) => score !== undefined) ?? null;
}
