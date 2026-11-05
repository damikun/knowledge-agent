import { normalizeRunStatus } from './polling';
import { duplicateKey } from './questionUpload';
import type { EvalDatasetDto, EvalDatasetItemDto, EvalItemSource, EvalRunSummaryDto, SaveEvalItemRequest } from './types';

export const ITEMS_PAGE_SIZE = 25;

export const SOURCE_ORDER: readonly EvalItemSource[] = ['conversation', 'manual', 'upload', 'copy'];

export const ITEM_SOURCE_LABELS: Readonly<Record<EvalItemSource, string>> = {
  conversation: 'Conversation',
  manual: 'Typed in',
  upload: 'Upload',
  copy: 'Copied',
};

export type ItemSourceFilter = 'all' | EvalItemSource;

export function itemSource(item: EvalDatasetItemDto): EvalItemSource {
  return item.source ?? (item.conversationId ? 'conversation' : 'manual');
}

/** Every word must match somewhere in the item. */
export function filterDatasetItems(items: readonly EvalDatasetItemDto[], query: string, source: ItemSourceFilter = 'all'): EvalDatasetItemDto[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  return items.filter((item) => {
    if (source !== 'all' && itemSource(item) !== source) {
      return false;
    }
    if (words.length === 0) {
      return true;
    }
    const text = [item.question, item.referenceAnswer ?? '', ...item.expectedFacts, ...item.tags, ...(item.history ?? []).map((turn) => turn.content)]
      .join('\n')
      .toLowerCase();
    return words.every((word) => text.includes(word));
  });
}

export function sourceCounts(items: readonly EvalDatasetItemDto[]): Record<EvalItemSource, number> {
  const counts: Record<EvalItemSource, number> = { conversation: 0, manual: 0, upload: 0, copy: 0 };
  for (const item of items) {
    counts[itemSource(item)]++;
  }
  return counts;
}

function pageCount(total: number, size = ITEMS_PAGE_SIZE): number {
  return Math.max(1, Math.ceil(total / size));
}

/** `page` is 0-based and clamped, so deleting the last rows never leaves an empty page. */
export function pageOf<T>(items: readonly T[], page: number, size = ITEMS_PAGE_SIZE): { rows: T[]; page: number } {
  const last = pageCount(items.length, size) - 1;
  const current = Math.min(Math.max(0, page), last);
  return { rows: items.slice(current * size, current * size + size), page: current };
}

export function expectedPreview(item: Pick<EvalDatasetItemDto, 'answerable' | 'referenceAnswer' | 'expectedFacts'>): string {
  if (!item.answerable) {
    return 'Should say it does not know';
  }
  if (item.referenceAnswer) {
    return item.referenceAnswer;
  }
  return item.expectedFacts.length > 0 ? `Must mention: ${item.expectedFacts.join('; ')}` : '';
}

/** Keeps the original's source. */
export function duplicateRequest(item: EvalDatasetItemDto): SaveEvalItemRequest {
  return {
    question: item.question,
    referenceAnswer: item.referenceAnswer,
    expectedFacts: item.expectedFacts,
    expectedSources: item.expectedSources,
    answerable: item.answerable,
    tags: item.tags,
    conversationId: item.conversationId,
    messageId: item.messageId,
    source: itemSource(item),
    history: item.history ?? [],
  };
}

export function existingKeys(items: readonly EvalDatasetItemDto[] | undefined): Set<string> {
  return new Set((items ?? []).map((item) => duplicateKey(item.question, item.history)));
}

/** Matched by name and source, as runs record them. */
export function latestCompletedRun(
  dataset: Pick<EvalDatasetDto, 'name' | 'source'>,
  runs: readonly EvalRunSummaryDto[] | undefined,
): EvalRunSummaryDto | null {
  let best: EvalRunSummaryDto | null = null;
  for (const run of runs ?? []) {
    if (
      run.dataset === dataset.name &&
      (run.datasetSource ?? '').toLowerCase() === dataset.source.toLowerCase() &&
      normalizeRunStatus(run.status) === 'completed' &&
      (!best || Date.parse(run.createdAt) > Date.parse(best.createdAt))
    ) {
      best = run;
    }
  }
  return best;
}

export function datasetPath(id: string): string {
  return `/evals/datasets/${encodeURIComponent(id)}`;
}
