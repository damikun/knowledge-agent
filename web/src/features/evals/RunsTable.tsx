import { CircleStop, ExternalLink, Trash } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { Button, buttonClass } from '../../components/ui/Button';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatDateTime, formatDuration, formatRelativeTime, pluralize, safeExternalUrl } from '../../lib/format';
import { GateBadge, RunProgress, RunStatusBadge } from './EvalBadges';
import { StatusDot } from '../../components/ui/StatusDot';
import { formatPercent, metricInfo } from './metrics';
import { headlineMetric, QUALITY_AREAS, VERDICTS, verdict } from './quality';
import { isRunActive } from './polling';
import { DATASET_SOURCE_LABELS, runDurationMs, runPath } from './runs';
import type { EvalRunSummaryDto } from './types';

export interface RunsTableProps {
  runs: EvalRunSummaryDto[];
  /** Fetch time; running runs measure elapsed time up to it. */
  now: number;
  /** Runs with a cancel request in flight. */
  busyIds: ReadonlySet<string>;
  onCancel: (run: EvalRunSummaryDto) => void;
  onDelete: (run: EvalRunSummaryDto) => void;
}

const missing = <span className="text-fg-muted">–</span>;

export function RunsTable({ runs, now, busyIds, onCancel, onDelete }: RunsTableProps) {
  const navigate = useNavigate();
  const areaColumns = QUALITY_AREAS.filter((area) => runs.some((run) => headlineMetric(area, run.metrics)));

  return (
    <Table aria-label="Runs" className="min-w-[44rem]">
      <TableHead>
        <TableHeader>Run</TableHeader>
        <TableHeader>Status</TableHeader>
        {areaColumns.map((area) => (
          <TableHeader key={area.id} numeric title={area.meaning}>
            <span className="inline-block py-1 whitespace-normal">{area.short}</span>
          </TableHeader>
        ))}
        <TableHeader>Gate</TableHeader>
        <TableHeader>Started</TableHeader>
        <TableHeader>
          <span className="sr-only">Actions</span>
        </TableHeader>
      </TableHead>
      <tbody>
        {runs.map((run) => {
          const active = isRunActive(run.status);
          const langfuseUrl = safeExternalUrl(run.langfuseUrl);
          const label = run.name || 'Untitled run';
          const source = DATASET_SOURCE_LABELS[run.datasetSource?.toLowerCase()];
          const busy = busyIds.has(run.id);
          const setup = [run.dataset, run.chatModel].filter(Boolean).join(' · ');
          return (
            <TableRow
              key={run.id}
              onClick={(event) => {
                // Links and buttons in the row handle their own clicks.
                if (!(event.target as HTMLElement).closest('a, button')) {
                  navigate(runPath(run.id));
                }
              }}
              className="cursor-pointer"
            >
              <TableRowHeader className="max-w-64">
                <Link to={runPath(run.id)} className="block truncate hover:underline" title={label}>
                  {label}
                </Link>
                <div className="truncate font-mono text-xs font-normal text-fg-muted" title={source ? `${setup} (${source.label} dataset)` : setup}>
                  {run.dataset}
                  {run.chatModel ? <span> · {run.chatModel}</span> : null}
                </div>
              </TableRowHeader>
              <TableCell>
                <RunStatusBadge run={run} />
                <RunProgress run={run} className="mt-1" />
              </TableCell>
              {areaColumns.map((area) => {
                const metric = headlineMetric(area, run.metrics);
                const value = metric ? run.metrics[metric] : undefined;
                const result = value ? VERDICTS[verdict(area, value.mean)] : null;
                return (
                  <TableCell
                    key={area.id}
                    numeric
                    className="whitespace-nowrap"
                    title={value && metric && result ? `${result.label} · ${metricInfo(metric).label} · ${pluralize(value.count, 'question')}` : undefined}
                  >
                    {value && result ? (
                      <span className="inline-flex items-center gap-1.5 text-fg">
                        <StatusDot tone={result.tone} />
                        {formatPercent(value.mean)}
                        <span className="sr-only">, {result.label}</span>
                      </span>
                    ) : (
                      missing
                    )}
                  </TableCell>
                );
              })}
              <TableCell>
                <GateBadge gate={run.gate} />
              </TableCell>
              <TableCell className="whitespace-nowrap">
                <div title={run.startedAt ? formatDateTime(run.startedAt) : undefined}>{run.startedAt ? formatRelativeTime(run.startedAt) : missing}</div>
                <div className="text-xs text-fg-muted tabular-nums">{formatDuration(runDurationMs(run, now))}</div>
              </TableCell>
              <TableCell className="py-1">
                {/* Always visible on devices that can't hover. */}
                <div className="flex justify-end gap-0.5 opacity-0 group-hover/row:opacity-100 group-focus-within/row:opacity-100 [@media(hover:none)]:opacity-100">
                  {langfuseUrl ? (
                    <a
                      href={langfuseUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      aria-label={`Open “${label}” in Langfuse`}
                      title="Open in Langfuse"
                      className={buttonClass({ variant: 'ghost', size: 'icon-sm' })}
                    >
                      <ExternalLink className="size-4" aria-hidden />
                    </a>
                  ) : null}
                  {active ? (
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      icon={CircleStop}
                      loading={busy}
                      aria-label={`Cancel “${label}”`}
                      title="Cancel run"
                      onClick={() => onCancel(run)}
                    />
                  ) : (
                    <Button variant="danger-ghost" size="icon-sm" icon={Trash} aria-label={`Delete “${label}”`} title="Delete run" onClick={() => onDelete(run)} />
                  )}
                </div>
              </TableCell>
            </TableRow>
          );
        })}
      </tbody>
    </Table>
  );
}
