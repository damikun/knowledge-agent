import { clsx } from 'clsx';
import { ChevronLeft, ChevronRight, FolderInput, RefreshCw } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { Spinner } from '../../components/ui/feedback';
import { Select } from '../../components/ui/Select';
import { pluralize } from '../../lib/format';
import { PAGE_SIZES, pageRangeLabel } from './sourceFilters';
import type { SourcePageDto } from './types';

const PAGE_SIZE_OPTIONS = PAGE_SIZES.map((size) => ({ value: String(size), label: String(size) }));

export interface SelectionBarProps {
  /** Such as "3 sources selected"; null while nothing is selected. */
  text: string | null;
  /** Offered when the whole page is selected and more sources match. */
  onSelectAll?: { total: number; select: () => void };
  onMove: () => void;
  onClear: () => void;
}

/** What is selected and what to do with it, above the table. */
export function SelectionBar({ text, onSelectAll, onMove, onClear }: SelectionBarProps) {
  // The count stays mounted while nothing is selected, so screen readers announce every change.
  return (
    <div className={clsx(text && 'flex flex-wrap items-center gap-2 border-b border-border-subtle bg-surface-subtle px-6 py-2')}>
      <span role="status" className={text ? 'mr-1 text-small font-medium text-fg' : 'sr-only'}>
        {text ?? ''}
      </span>
      {text ? (
        <>
          {onSelectAll ? (
            <Button size="sm" variant="ghost" onClick={onSelectAll.select}>
              Select all {pluralize(onSelectAll.total, 'source')}
            </Button>
          ) : null}
          <Button size="sm" icon={FolderInput} onClick={onMove}>
            Move to folder
          </Button>
          <Button size="sm" variant="ghost" onClick={onClear}>
            Clear selection
          </Button>
        </>
      ) : null}
    </div>
  );
}

export interface SourcesPagerProps {
  page: SourcePageDto;
  /** The previous page shows while the next one loads. */
  placeholder: boolean;
  pageSize: number;
  onPageSize: (size: number) => void;
  polling: boolean;
  refreshing: boolean;
  onRefresh: () => void;
  onShowPage: (offset: number) => void;
}

/** The range shown, the page size, a refresh and the page buttons, below the table. */
export function SourcesPager({ page, placeholder, pageSize, onPageSize, polling, refreshing, onRefresh, onShowPage }: SourcesPagerProps) {
  const multiplePages = page.offset > 0 || page.nextOffset !== null;
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t border-border-subtle px-6 py-2">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-xs text-fg-muted tabular-nums" aria-live="polite">
          {pageRangeLabel(page)}
        </span>
        {page.total > PAGE_SIZES[0] ? (
          <span className="flex items-center gap-1.5 text-xs text-fg-muted">
            Per page
            <Select
              aria-label="Sources per page"
              size="sm"
              options={PAGE_SIZE_OPTIONS}
              value={String(pageSize)}
              onChange={(value) => onPageSize(Number(value))}
              className="w-18"
            />
          </span>
        ) : null}
      </div>
      <div className="flex items-center gap-1.5">
        {polling ? <Spinner label="Refreshing every 2 s" className="size-3.5" /> : null}
        <Button size="icon-sm" variant="ghost" icon={RefreshCw} aria-label="Refresh" title="Refresh" onClick={onRefresh} loading={refreshing} />
        {multiplePages ? (
          <>
            <Button size="sm" icon={ChevronLeft} disabled={page.offset === 0 || placeholder} onClick={() => onShowPage(page.offset - pageSize)}>
              Previous
            </Button>
            <Button size="sm" disabled={page.nextOffset === null || placeholder} onClick={() => page.nextOffset !== null && onShowPage(page.nextOffset)}>
              Next
              <ChevronRight className="size-3.5" aria-hidden />
            </Button>
          </>
        ) : null}
      </div>
    </div>
  );
}
