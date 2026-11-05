import { clsx } from 'clsx';
import { ChevronRight, CircleCheck, CircleX, ListChecks } from 'lucide-react';
import { Fragment, useId, useMemo, useState } from 'react';
import { Badge } from '../../components/ui/Badge';
import { EmptyState } from '../../components/ui/feedback';
import { FilterChips, type FilterChipOption } from '../../components/ui/FilterChips';
import { Table, TableCell, TableHead, TableHeader, TableRow } from '../../components/ui/Table';
import { formatDuration } from '../../lib/format';
import { formatPercent, metricInfo } from './metrics';
import { filterItems, isFailedItem, itemAreaMetrics, missedArea, QUALITY_AREAS, qualityArea, verdict, type ItemFilter, type QualityAreaId, type Verdict } from './quality';
import { ItemDetails } from './RunItemDetails';
import type { EvalItemResultDto } from './types';

const SCORE_CLASSES: Record<Verdict, string> = {
  good: 'text-success-text',
  'needs-work': 'text-warning-text',
  poor: 'text-danger-text',
};

function ItemStatusBadge({ item }: { item: EvalItemResultDto }) {
  return item.status?.toLowerCase() === 'failed' ? (
    <Badge tone="danger" title={item.error ?? 'The agent turn failed'}>
      <CircleX className="size-3" aria-hidden />
      Failed
    </Badge>
  ) : (
    <Badge tone="success">
      <CircleCheck className="size-3" aria-hidden />
      Completed
    </Badge>
  );
}

export interface ItemsTableProps {
  items: EvalItemResultDto[];
  active: boolean;
  filter?: ItemFilter;
  onFilterChange?: (filter: ItemFilter) => void;
}

export function ItemsTable({ items: allItems, active, filter: controlledFilter, onFilterChange }: ItemsTableProps) {
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(new Set());
  const [ownFilter, setOwnFilter] = useState<ItemFilter>('all');
  const filter = controlledFilter ?? ownFilter;
  const setFilter = onFilterChange ?? setOwnFilter;
  const idPrefix = useId();
  // One metric per area, so columns and filters match the score cards.
  const areaMetrics = useMemo(() => itemAreaMetrics(allItems), [allItems]);
  const areaColumns = QUALITY_AREAS.flatMap((area) => (areaMetrics[area.id] ? [{ area, metric: areaMetrics[area.id]! }] : []));
  const items = useMemo(() => filterItems(allItems, filter, areaMetrics), [allItems, filter, areaMetrics]);
  const columnCount = 4 + areaColumns.length;

  if (allItems.length === 0) {
    return (
      <EmptyState
        icon={ListChecks}
        title={active ? 'No questions answered yet' : 'No results'}
        description={active ? 'Results appear here as the questions are answered.' : 'This run recorded no question results.'}
      />
    );
  }

  const failed = allItems.filter(isFailedItem).length;
  const filterOptions: FilterChipOption<ItemFilter>[] = [
    { value: 'all', label: 'All', count: allItems.length },
    ...areaColumns.flatMap(({ area, metric }) => {
      const count = allItems.filter((item) => missedArea(item, area, metric)).length;
      return count > 0 || filter === area.id ? [{ value: area.id as ItemFilter, label: area.missedLabel, count }] : [];
    }),
    ...(failed > 0 || filter === 'errors' ? [{ value: 'errors' as ItemFilter, label: 'Errors', count: failed, dot: 'danger' as const }] : []),
  ];

  const toggle = (itemId: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (!next.delete(itemId)) {
        next.add(itemId);
      }
      return next;
    });

  const table = (
    <Table aria-label="Questions" className="min-w-[44rem]">
      <TableHead>
        <TableHeader className="w-10">
          <span className="sr-only">Number</span>
        </TableHeader>
        <TableHeader>Question</TableHeader>
        <TableHeader>Status</TableHeader>
        {areaColumns.map(({ area, metric }) => (
          <TableHeader key={area.id} numeric title={`${metricInfo(metric).label}: ${metricInfo(metric).description}`}>
            <span className="inline-block py-1 whitespace-normal">{area.short}</span>
          </TableHeader>
        ))}
        <TableHeader numeric>Duration</TableHeader>
      </TableHead>
      <tbody>
        {items.map((item) => {
          const index = allItems.indexOf(item);
          const open = expanded.has(item.itemId);
          const detailsId = `${idPrefix}-item-${index}`;
          return (
            <Fragment key={item.itemId}>
              <TableRow className={clsx(open && 'bg-surface-subtle')}>
                <TableCell className="text-xs text-fg-muted tabular-nums">{index + 1}</TableCell>
                <TableCell className="max-w-[32rem]">
                  <button
                    type="button"
                    aria-expanded={open}
                    aria-controls={detailsId}
                    onClick={() => toggle(item.itemId)}
                    className="flex w-full items-start gap-1.5 rounded-sm text-left"
                  >
                    <ChevronRight className={clsx('mt-0.5 size-4 shrink-0 text-fg-muted transition-transform duration-100 ease-out', open && 'rotate-90')} aria-hidden />
                    <span className="min-w-0">
                      <span className="line-clamp-2 font-medium break-words text-fg">{item.question}</span>
                      {item.tags.length > 0 || !item.answerable ? (
                        <span className="mt-0.5 block text-xs text-fg-muted">{[...(item.answerable ? [] : ['should decline']), ...item.tags].join(' · ')}</span>
                      ) : null}
                    </span>
                  </button>
                </TableCell>
                <TableCell>
                  <ItemStatusBadge item={item} />
                </TableCell>
                {areaColumns.map(({ area, metric }) => {
                  const value = item.metrics?.[metric];
                  return (
                    <TableCell key={area.id} numeric className="whitespace-nowrap">
                      {value === undefined || value === null ? (
                        <span className="text-fg-muted">–</span>
                      ) : (
                        <span className={SCORE_CLASSES[verdict(area, value)]}>{formatPercent(value)}</span>
                      )}
                    </TableCell>
                  );
                })}
                <TableCell numeric className="whitespace-nowrap">
                  {formatDuration(item.durationMs)}
                </TableCell>
              </TableRow>
              {open ? (
                <tr id={detailsId}>
                  <td colSpan={columnCount} className="border-b border-border-subtle bg-surface-subtle px-6 pt-3 pb-5">
                    <ItemDetails item={item} />
                  </td>
                </tr>
              ) : null}
            </Fragment>
          );
        })}
      </tbody>
    </Table>
  );

  return (
    <>
      {filterOptions.length > 1 ? (
        <div className="border-b border-border-subtle px-4 py-3">
          <FilterChips label="Show questions" options={filterOptions} value={filter} onChange={setFilter} />
        </div>
      ) : null}
      {items.length === 0 ? (
        <EmptyState icon={ListChecks} title="No questions here" description="No question in this run matches the filter." />
      ) : (
        table
      )}
      {filter !== 'all' && filter !== 'errors' ? (
        <p className="border-t border-border-subtle px-4 py-2.5 text-xs text-fg-muted">
          Questions that scored below {formatPercent(qualityArea(filter as QualityAreaId).thresholds.good)}, the line for Good, in “{qualityArea(filter as QualityAreaId).short}”. Open one to see what went wrong.
        </p>
      ) : null}
    </>
  );
}
