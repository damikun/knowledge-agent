import { describe, expect, it } from 'vitest';
import { evalItem } from '../../test/evalsApi';
import {
  changeOf,
  filterItems,
  headlineMetric,
  itemAreaMetrics,
  missedArea,
  QUALITY_AREAS,
  qualityArea,
  headlineScore,
  scoreAreas,
  verdict,
} from './quality';
import { EVAL_METRICS } from './metrics';

const value = (mean: number, count = 10) => ({ mean, count });

describe('quality areas', () => {
  it('asks four plain questions, each backed by known metric keys', () => {
    expect(QUALITY_AREAS.map((area) => area.title)).toEqual([
      'Finds the right information',
      'Answers correctly',
      'Backs answers with sources',
      'Says “I don’t know” when it should',
    ]);
    const known = new Set(EVAL_METRICS.map((metric) => metric.name));
    for (const area of QUALITY_AREAS) {
      expect(area.meaning).not.toBe('');
      expect(area.improve).not.toBe('');
      expect(area.thresholds.good).toBeGreaterThan(area.thresholds.fair);
      for (const name of [...area.headline, ...area.supporting]) {
        expect(known.has(name)).toBe(true);
      }
      expect(`${area.title}${area.short}${area.meaning}${area.improve}${area.missedLabel}`).not.toContain('—');
    }
  });

  it('maps each area to the first metric the run measured', () => {
    const finds = qualityArea('finds');
    expect(headlineMetric(finds, { retrieval_recall_at_5: value(0.9), agent_source_recall: value(0.5) })).toBe('retrieval_recall_at_5');
    expect(headlineMetric(finds, { agent_source_recall: value(0.5) })).toBe('agent_source_recall');
    expect(headlineMetric(finds, {})).toBeNull();
    const answers = qualityArea('answers');
    expect(headlineMetric(answers, { judge_correctness: value(0.7), answer_fact_recall: value(0.8) })).toBe('answer_fact_recall');
    expect(headlineMetric(answers, { judge_correctness: value(0.7) })).toBe('judge_correctness');
    expect(headlineMetric(qualityArea('cites'), { citation_validity: value(1) })).toBe('citation_validity');
    expect(headlineMetric(qualityArea('declines'), { answer_abstained: value(1, 2) })).toBe('answer_abstained');
  });
});

describe('verdict thresholds', () => {
  it.each([
    ['finds', 0.85, 'good'],
    ['finds', 0.849, 'needs-work'],
    ['finds', 0.6, 'needs-work'],
    ['finds', 0.599, 'poor'],
    ['answers', 0.8, 'good'],
    ['answers', 0.79, 'needs-work'],
    ['answers', 0.6, 'needs-work'],
    ['answers', 0.59, 'poor'],
    ['cites', 0.8, 'good'],
    ['cites', 0.6, 'needs-work'],
    ['cites', 0.2, 'poor'],
    ['declines', 0.9, 'good'],
    ['declines', 0.89, 'needs-work'],
    ['declines', 0.7, 'needs-work'],
    ['declines', 0.69, 'poor'],
  ] as const)('%s at %d is %s', (area, score, expected) => {
    expect(verdict(qualityArea(area), score)).toBe(expected);
  });
});

describe('change versus the previous run', () => {
  it('hides noise and keeps whole points', () => {
    expect(changeOf(null)).toBeNull();
    expect(changeOf(Number.NaN)).toBeNull();
    expect(changeOf(0)).toEqual({ kind: 'same' });
    expect(changeOf(0.9)).toEqual({ kind: 'same' });
    expect(changeOf(-0.9)).toEqual({ kind: 'same' });
    expect(changeOf(1)).toEqual({ kind: 'better', points: 1 });
    expect(changeOf(4.6)).toEqual({ kind: 'better', points: 5 });
    expect(changeOf(-5)).toEqual({ kind: 'worse', points: -5 });
  });
});

describe('scoreAreas', () => {
  it('scores the measured areas with verdict, change and supporting metrics', () => {
    const scores = scoreAreas(
      {
        retrieval_recall_at_5: value(0.9),
        retrieval_recall_at_10: value(0.95),
        answer_fact_recall: value(0.55),
        judge_faithfulness: value(0.9),
        citation_source_recall: value(0.7),
        citation_validity: value(1),
      },
      { retrieval_recall_at_5: value(0.8), answer_fact_recall: value(0.552), citation_source_recall: value(0.9) },
    );
    expect(scores.map((score) => [score.area.id, score.metric, score.verdict, score.change])).toEqual([
      ['finds', 'retrieval_recall_at_5', 'good', { kind: 'better', points: 10 }],
      ['answers', 'answer_fact_recall', 'poor', { kind: 'same' }],
      ['cites', 'citation_source_recall', 'needs-work', { kind: 'worse', points: -20 }],
    ]);
    expect(scores[1]!.supporting).toEqual(['judge_faithfulness']);
    expect(scores[2]!.supporting).toEqual(['citation_validity']);
    expect(scoreAreas({ answer_abstained: value(1, 2) }, null)[0]).toMatchObject({ metric: 'answer_abstained', change: null });
  });
});

describe('questions that fell short', () => {
  const items = [
    evalItem({ itemId: 'a', metrics: { retrieval_recall_at_5: 1, answer_fact_recall: 0.5 } }),
    evalItem({ itemId: 'b', metrics: { retrieval_recall_at_5: 0.5, answer_fact_recall: 1 } }),
    evalItem({ itemId: 'c', answerable: false, metrics: { answer_abstained: 0 } }),
    evalItem({ itemId: 'd', status: 'failed', metrics: {} }),
  ];

  it('filters by the area headline below the Good line, and by errors', () => {
    const metrics = itemAreaMetrics(items);
    expect(metrics).toEqual({ finds: 'retrieval_recall_at_5', answers: 'answer_fact_recall', declines: 'answer_abstained' });
    expect(filterItems(items, 'all', metrics).map((item) => item.itemId)).toEqual(['a', 'b', 'c', 'd']);
    expect(filterItems(items, 'finds', metrics).map((item) => item.itemId)).toEqual(['b']);
    expect(filterItems(items, 'answers', metrics).map((item) => item.itemId)).toEqual(['a']);
    expect(filterItems(items, 'declines', metrics).map((item) => item.itemId)).toEqual(['c']);
    expect(filterItems(items, 'errors', metrics).map((item) => item.itemId)).toEqual(['d']);
    expect(filterItems(items, 'cites', metrics)).toEqual([]);
  });

  it('never counts a question the metric does not apply to', () => {
    expect(missedArea(items[2]!, qualityArea('finds'), 'retrieval_recall_at_5')).toBe(false);
    expect(missedArea(items[0]!, qualityArea('finds'), 'retrieval_recall_at_5')).toBe(false);
  });
});

describe('headlineScore', () => {
  it('sums a run up by its answers, and falls back to what it measured', () => {
    expect(headlineScore({ answer_fact_recall: { mean: 0.7, count: 4 }, retrieval_recall_at_5: { mean: 0.95, count: 4 } })).toMatchObject({
      area: { id: 'answers' },
      verdict: 'needs-work',
    });
    expect(headlineScore({ retrieval_recall_at_5: { mean: 0.5, count: 4 } })).toMatchObject({ area: { id: 'finds' }, verdict: 'poor' });
    expect(headlineScore({})).toBeNull();
    expect(headlineScore(null)).toBeNull();
  });
});
