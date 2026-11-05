import { describe, expect, it } from 'vitest';
import { evalRunSummary } from '../../test/evalsApi';
import { duplicateRequest, existingKeys, expectedPreview, filterDatasetItems, itemSource, latestCompletedRun, pageOf, sourceCounts } from './datasetItems';
import { duplicateKey } from './questionUpload';
import type { EvalDatasetItemDto } from './types';

function item(overrides: Partial<EvalDatasetItemDto> = {}): EvalDatasetItemDto {
  return {
    id: 'i1',
    question: 'Do you ship to Norway?',
    referenceAnswer: null,
    expectedFacts: [],
    expectedSources: [],
    answerable: true,
    tags: [],
    conversationId: null,
    messageId: null,
    createdAt: '2026-11-04T10:00:00Z',
    updatedAt: '2026-11-04T10:00:00Z',
    source: 'manual',
    history: [],
    ...overrides,
  };
}

describe('dataset questions', () => {
  it('reads the source of rows from before the column from the conversation link', () => {
    expect(itemSource(item({ source: undefined }))).toBe('manual');
    expect(itemSource(item({ source: undefined, conversationId: 'c1' }))).toBe('conversation');
    expect(itemSource(item({ source: 'upload' }))).toBe('upload');
  });

  it('searches every word in the question, answer, facts, tags and earlier turns, and filters by source', () => {
    const items = [
      item({ id: 'a', question: 'Do you ship to Norway?', tags: ['shipping'] }),
      item({ id: 'b', question: 'And sale items?', history: [{ role: 'user', content: 'Can I return shoes?' }], source: 'upload' }),
      item({ id: 'c', question: 'Opening hours?', expectedFacts: ['9 to 5'], source: 'conversation' }),
    ];
    expect(filterDatasetItems(items, 'SHIP norway').map((entry) => entry.id)).toEqual(['a']);
    expect(filterDatasetItems(items, 'return shoes').map((entry) => entry.id)).toEqual(['b']);
    expect(filterDatasetItems(items, '9 to').map((entry) => entry.id)).toEqual(['c']);
    expect(filterDatasetItems(items, '', 'upload').map((entry) => entry.id)).toEqual(['b']);
    expect(sourceCounts(items)).toEqual({ manual: 1, upload: 1, conversation: 1, copy: 0 });
  });

  it('pages and clamps to the last page', () => {
    const numbers = Array.from({ length: 30 }, (_, index) => index);
    expect(pageOf(numbers, 1, 25)).toEqual({ rows: [25, 26, 27, 28, 29], page: 1 });
    expect(pageOf(numbers, 5, 25).page).toBe(1);
    expect(pageOf([], 3, 25)).toEqual({ rows: [], page: 0 });
  });

  it('previews what a good reply says', () => {
    expect(expectedPreview(item({ referenceAnswer: 'Yes.', expectedFacts: ['5 days'] }))).toBe('Yes.');
    expect(expectedPreview(item({ expectedFacts: ['5 days', 'Norway'] }))).toBe('Must mention: 5 days; Norway');
    expect(expectedPreview(item({ answerable: false }))).toBe('Should say it does not know');
    expect(expectedPreview(item())).toBe('');
  });

  it('duplicates a question with its origin and history', () => {
    const original = item({ conversationId: 'c1', messageId: 'm1', source: 'conversation', history: [{ role: 'user', content: 'Hi' }], tags: ['a'] });
    expect(duplicateRequest(original)).toMatchObject({ question: original.question, conversationId: 'c1', messageId: 'm1', source: 'conversation', history: [{ role: 'user', content: 'Hi' }], tags: ['a'] });
  });

  it('keys existing questions the way uploads compare them', () => {
    expect(existingKeys([item({ question: 'Do  you ship?' })]).has(duplicateKey('do you ship?'))).toBe(true);
  });
});

describe('latestCompletedRun', () => {
  it('picks the newest completed run of the same name and source', () => {
    const dataset = { name: 'Shipping', source: 'custom' as const };
    const runs = [
      evalRunSummary({ id: 'old', dataset: 'Shipping', datasetSource: 'custom', createdAt: '2026-11-01T10:00:00Z' }),
      evalRunSummary({ id: 'new', dataset: 'Shipping', datasetSource: 'custom', createdAt: '2026-11-03T10:00:00Z' }),
      evalRunSummary({ id: 'running', dataset: 'Shipping', datasetSource: 'custom', status: 'running', createdAt: '2026-11-04T10:00:00Z' }),
      evalRunSummary({ id: 'langfuse', dataset: 'Shipping', datasetSource: 'langfuse', createdAt: '2026-11-05T10:00:00Z' }),
    ];
    expect(latestCompletedRun(dataset, runs)?.id).toBe('new');
    expect(latestCompletedRun(dataset, [])).toBeNull();
  });
});
