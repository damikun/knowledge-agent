import { clsx } from 'clsx';
import { Table2 } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Button } from '../../components/ui/Button';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatNumber } from '../../lib/format';

interface ChartSeries {
  key: string;
  label: string;
  fill: string;
  values: readonly number[];
}

export interface DailyChartProps {
  label: string;
  /** yyyy-mm-dd, one per column. */
  dates: readonly string[];
  /**
   * stacked: the series add up in one column, the first at the bottom. diverging: the first series above the baseline
   * and the second below it, for good against bad.
   */
  mode: 'stacked' | 'diverging';
  series: readonly ChartSeries[];
  summary: string;
  /** Extra lines of a day's tooltip; only for daily columns, since a week sums several days. */
  details?: (index: number) => ReactNode;
  emptyLabel?: string;
}

// Past this many days, columns become weeks: 90 one-pixel bars read as noise, 13 weekly ones as a trend.
const WEEKLY_FROM = 45;

interface Bucket {
  /** The first day, yyyy-mm-dd. */
  start: string;
  days: number;
}

/** Days, or weeks of up to 7 days ending on the last day, so the newest column is always a full week. */
function bucketsOf(dates: readonly string[]): Bucket[] {
  if (dates.length < WEEKLY_FROM) {
    return dates.map((start) => ({ start, days: 1 }));
  }
  const buckets: Bucket[] = [];
  for (let end = dates.length; end > 0; end -= 7) {
    const begin = Math.max(0, end - 7);
    buckets.unshift({ start: dates[begin] ?? '', days: end - begin });
  }
  return buckets;
}

const dayFormat = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' });
const weekdayFormat = new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'UTC' });

function toDate(day: string): Date {
  return new Date(`${day}T00:00:00Z`);
}

function formatDay(day: string): string {
  const date = toDate(day);
  return Number.isNaN(date.getTime()) ? day : dayFormat.format(date);
}

function niceMax(value: number): number {
  if (value <= 4) {
    return 4;
  }
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const step = [1, 2, 2.5, 5, 10].map((factor) => factor * magnitude).find((candidate) => candidate * 2 >= value) ?? magnitude * 10;
  return step * 2;
}

function Legend({ series }: { series: readonly ChartSeries[] }) {
  return (
    <ul className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-fg-secondary" aria-hidden>
      {series.map((item) => (
        <li key={item.key} className="inline-flex items-center gap-1.5">
          <span className={clsx('size-2.5 rounded-[2px]', item.fill)} />
          {item.label}
        </li>
      ))}
    </ul>
  );
}

/** Plain HTML columns, so they size with the card. The same numbers are in a table for screen readers. */
export function DailyChart({
  label,
  dates: days,
  mode,
  series: dailySeries,
  summary,
  details,
  emptyLabel = 'Nothing in this period',
}: DailyChartProps) {
  const id = useId();
  const [active, setActive] = useState<number | null>(null);
  const [showTable, setShowTable] = useState(false);
  const diverging = mode === 'diverging';
  const buckets = bucketsOf(days);
  const weekly = buckets.length < days.length;
  const dates = buckets.map((bucket) => bucket.start);
  let offset = 0;
  const ranges = buckets.map((bucket) => {
    const range = [offset, offset + bucket.days] as const;
    offset += bucket.days;
    return range;
  });
  const series = weekly
    ? dailySeries.map((item) => ({ ...item, values: ranges.map(([from, to]) => item.values.slice(from, to).reduce((sum, value) => sum + value, 0)) }))
    : dailySeries;
  const totals = dates.map((_, index) => series.reduce((sum, item) => sum + (item.values[index] ?? 0), 0));
  const empty = totals.every((total) => total === 0);
  const max = niceMax(diverging ? Math.max(0, ...series.flatMap((item) => item.values)) : Math.max(0, ...totals));
  const labelled = new Set(dates.length <= 7 ? dates.map((_, index) => index) : [0, Math.floor((dates.length - 1) / 2), dates.length - 1]);
  const half = (value: number) => `${(value / max) * 100}%`;
  const heading = (index: number) => {
    const start = dates[index] ?? '';
    return weekly ? `Week of ${formatDay(start)}` : `${formatDay(start)}, ${weekdayFormat.format(toDate(start))}`;
  };

  const column = (index: number) => {
    if (diverging) {
      const [up, down] = series;
      return (
        <>
          <div className="flex h-1/2 flex-col justify-end">
            {up && up.values[index] ? <div className={clsx('rounded-t-sm', up.fill)} style={{ height: half(up.values[index]) }} /> : null}
          </div>
          <div className="flex h-1/2 flex-col justify-start pt-0.5">
            {down && down.values[index] ? <div className={clsx('rounded-b-sm', down.fill)} style={{ height: half(down.values[index]) }} /> : null}
          </div>
        </>
      );
    }
    const present = series.filter((item) => (item.values[index] ?? 0) > 0);
    return (
      <div className="flex h-full flex-col-reverse gap-0.5">
        {present.map((item, position) => (
          <div
            key={item.key}
            className={clsx(item.fill, position === present.length - 1 && 'rounded-t-sm')}
            style={{ height: `calc(${half(item.values[index] ?? 0)} - 2px)` }}
          />
        ))}
      </div>
    );
  };

  return (
    <figure className="space-y-3" aria-labelledby={`${id}-caption`}>
      <figcaption id={`${id}-caption`} className="sr-only">
        {label}. {summary}
      </figcaption>
      {/* Read top to bottom like the stack: the first series sits at the bottom, so it is listed last. */}
      <Legend series={diverging ? series : [...series].reverse()} />
      {weekly ? <p className="text-xs text-fg-muted">One column per week.</p> : null}
      <div className="relative" onMouseLeave={() => setActive(null)} aria-hidden>
        <div className="flex">
          <div className={clsx('relative w-8 shrink-0 text-right text-[11px] text-fg-muted tabular-nums', empty && 'invisible')}>
            <span className="absolute top-0 right-2 -translate-y-1/2">{diverging ? `+${formatNumber(max)}` : formatNumber(max)}</span>
            {diverging ? (
              <>
                <span className="absolute top-1/2 right-2 -translate-y-1/2">0</span>
                <span className="absolute right-2 bottom-0 translate-y-1/2">−{formatNumber(max)}</span>
              </>
            ) : (
              <span className="absolute right-2 bottom-0 translate-y-1/2">0</span>
            )}
          </div>
          <div className="relative h-44 flex-1">
            <div className={clsx('pointer-events-none absolute inset-x-0 top-0 border-t border-border-subtle', empty && 'hidden')} />
            <div className={clsx('pointer-events-none absolute inset-x-0 top-1/2 border-t border-border-subtle', empty && 'hidden')} />
            <div className={clsx('pointer-events-none absolute inset-x-0 bottom-0 border-t', diverging ? 'border-border-subtle' : 'border-border')} />
            {diverging && !empty ? <div className="pointer-events-none absolute inset-x-0 top-1/2 border-t border-border" /> : null}
            {empty ? <p className="absolute inset-0 flex items-center justify-center text-small text-fg-muted">{emptyLabel}</p> : null}
            <div className={clsx('absolute inset-0 flex gap-0.5', empty && 'hidden')}>
              {dates.map((day, index) => (
                <div
                  key={day}
                  className={clsx('relative flex h-full min-w-0 flex-1 flex-col', active === index && 'bg-fill-hover')}
                  onMouseEnter={() => setActive(index)}
                >
                  <div className="mx-auto flex h-full w-[70%] max-w-6 min-w-1 flex-col">{column(index)}</div>
                </div>
              ))}
            </div>
            {active !== null ? (
              // Beside the hovered column, inside the plot: to its right in the left half, to its left in the right half.
              <div
                className="pointer-events-none absolute top-2 z-10 w-max max-w-64 rounded-lg border border-border bg-raised p-2.5 text-xs shadow-menu"
                style={
                  active / dates.length < 0.5
                    ? { left: `calc(${((active + 1) / dates.length) * 100}% + 8px)` }
                    : { left: `calc(${(active / dates.length) * 100}% - 8px)`, transform: 'translateX(-100%)' }
                }
              >
                <p className="font-medium text-fg">{heading(active)}</p>
                <ul className="mt-1.5 space-y-1">
                  {(diverging ? series : [...series].reverse()).map((item) => (
                    <li key={item.key} className="flex items-center justify-between gap-3 text-fg-secondary">
                      <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
                        <span className={clsx('size-2 shrink-0 rounded-[2px]', item.fill)} />
                        {item.label}
                      </span>
                      <span className="font-medium text-fg tabular-nums">{formatNumber(item.values[active] ?? 0)}</span>
                    </li>
                  ))}
                </ul>
                {details && !weekly ? <div className="mt-1.5 border-t border-border-subtle pt-1.5 text-fg-muted">{details(active)}</div> : null}
              </div>
            ) : null}
          </div>
        </div>
        <div className={clsx('ml-8 flex h-5 text-[11px] text-fg-muted', empty && 'invisible')}>
          {dates.map((day, index) => (
            <div key={day} className="relative min-w-0 flex-1">
              {labelled.has(index) ? (
                <span
                  className={clsx(
                    'absolute top-1 whitespace-nowrap',
                    index === 0 && dates.length > 7 ? 'left-0' : index === dates.length - 1 && dates.length > 7 ? 'right-0' : 'left-1/2 -translate-x-1/2',
                  )}
                >
                  {dates.length <= 7 && !weekly ? weekdayFormat.format(toDate(day)) : formatDay(day)}
                </span>
              ) : null}
            </div>
          ))}
        </div>
      </div>
      <Button size="sm" variant="ghost" icon={Table2} aria-expanded={showTable} aria-controls={`${id}-table`} onClick={() => setShowTable((shown) => !shown)}>
        {showTable ? 'Hide the numbers' : 'Show the numbers'}
      </Button>
      <div id={`${id}-table`} className={showTable ? 'max-h-72 overflow-y-auto rounded-lg border border-border-subtle' : 'sr-only'}>
        <Table aria-label={label}>
          <TableHead>
            <TableHeader>Day</TableHeader>
            {dailySeries.map((item) => (
              <TableHeader key={item.key} numeric>
                {item.label}
              </TableHeader>
            ))}
          </TableHead>
          <tbody>
            {days.map((day, index) => (
              <TableRow key={day}>
                <TableRowHeader>{formatDay(day)}</TableRowHeader>
                {dailySeries.map((item) => (
                  <TableCell key={item.key} numeric>
                    {formatNumber(item.values[index] ?? 0)}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </tbody>
        </Table>
      </div>
    </figure>
  );
}
