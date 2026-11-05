import type { FilterChipOption } from '../../components/ui/FilterChips';
import type { Tone } from '../../components/ui/tone';
import { formatNumber, pluralize } from '../../lib/format';
import { ingestingCount } from './polling';
import type { SourceListParams, SourcePageDto, SourceStatsDto, SourceStatus, SourceType } from './types';

export const SOURCES_PAGE_SIZE = 25;
export const PAGE_SIZES = [25, 50, 100] as const;

export type TypeFilter = SourceType | 'all';
export type StatusFilter = 'all' | 'ready' | 'indexing' | 'attention';

export const TYPE_FILTERS: ReadonlyArray<{ value: TypeFilter; label: string }> = [
  { value: 'all', label: 'All types' },
  { value: 'url', label: 'Web page' },
  { value: 'pdf', label: 'PDF' },
  { value: 'text', label: 'Text' },
  { value: 'markdown', label: 'Markdown' },
  { value: 'website', label: 'Website' },
];

const STATUS_FILTERS: ReadonlyArray<{ value: StatusFilter; label: string; statuses: readonly SourceStatus[] }> = [
  { value: 'all', label: 'All', statuses: [] },
  { value: 'ready', label: 'Ready', statuses: ['ready'] },
  { value: 'indexing', label: 'Indexing', statuses: ['pending', 'processing'] },
  // Both wait for someone: a failed source for a retry, a waiting one for an embedding model.
  { value: 'attention', label: 'Needs attention', statuses: ['failed', 'waiting'] },
];

export interface SourceFilters {
  /** Debounced and trimmed. */
  query: string;
  type: TypeFilter;
  status: StatusFilter;
  /** Empty for every site. */
  site?: string;
}

export function hasActiveFilters(filters: SourceFilters): boolean {
  return filters.query !== '' || filters.type !== 'all' || filters.status !== 'all' || Boolean(filters.site);
}

export function toListParams(filters: SourceFilters, offset: number, limit = SOURCES_PAGE_SIZE): SourceListParams {
  return {
    query: filters.query,
    type: filters.type === 'all' ? null : filters.type,
    status: STATUS_FILTERS.find((option) => option.value === filters.status)?.statuses ?? [],
    site: filters.site || null,
    offset,
    limit,
  };
}

export function lastPageOffset(total: number, pageSize = SOURCES_PAGE_SIZE): number {
  return total <= 0 ? 0 : Math.floor((total - 1) / pageSize) * pageSize;
}

/** "Showing 26–50 of 1,240". */
export function pageRangeLabel(page: Pick<SourcePageDto, 'items' | 'offset' | 'total'>): string {
  if (page.items.length === 0) {
    return '';
  }
  return `Showing ${formatNumber(page.offset + 1)}–${formatNumber(page.offset + page.items.length)} of ${formatNumber(page.total)}`;
}

export interface LibrarySummaryItem {
  label: string;
  /** The color of its status dot; the total has none. */
  tone?: Tone;
}

/** "1,240 sources", "1,200 ready", "12 indexing", "8 need attention"; the last two only when there are any. */
export function librarySummary(stats: SourceStatsDto): LibrarySummaryItem[] {
  const items: LibrarySummaryItem[] = [{ label: pluralize(stats.total, 'source') }, { label: `${formatNumber(stats.ready)} ready`, tone: 'success' }];
  const indexing = ingestingCount(stats);
  if (indexing > 0) {
    items.push({ label: `${formatNumber(indexing)} indexing`, tone: 'info' });
  }
  const attention = stats.failed + stats.waiting;
  if (attention > 0) {
    items.push({
      label: `${formatNumber(attention)} ${attention === 1 ? 'needs' : 'need'} attention`,
      tone: stats.failed > 0 ? 'danger' : 'warning',
    });
  }
  return items;
}

/**
 * The status chips with the library's counts, which replace a separate summary line. The stats cover the whole library,
 * so the counts show only where the list does too: in All sources without a type, site or search filter.
 */
export function statusFilterOptions(stats: SourceStatsDto | undefined, withCounts: boolean): FilterChipOption<StatusFilter>[] {
  const attention = stats ? stats.failed + stats.waiting : 0;
  const counts: Record<StatusFilter, number> | null =
    stats && withCounts ? { all: stats.total, ready: stats.ready, indexing: ingestingCount(stats), attention } : null;
  return STATUS_FILTERS.map(({ value, label }) => ({
    value,
    label,
    count: counts?.[value],
    // A dot draws the eye to sources that need someone, wherever the list is.
    dot: value === 'attention' && attention > 0 ? (stats!.failed > 0 ? 'danger' : 'warning') : undefined,
  }));
}
