import { clsx } from 'clsx';
import { ArrowDown, ArrowRight, ArrowUp, ChartColumn, CircleCheck, CircleX } from 'lucide-react';
import { Badge } from '../../components/ui/Badge';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { Card, EmptyState } from '../../components/ui/feedback';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatNumber, pluralize } from '../../lib/format';
import { GateBadge } from './EvalBadges';
import { describeGateCheck, formatDelta, formatPercent, gateCheckKindLabel, metricDelta, metricInfo, presentMetrics } from './metrics';
import { changeOf, headlineMetric, QUALITY_AREAS, scoreAreas, VERDICTS, type AreaScore, type Change, type QualityAreaId } from './quality';
import type { EvalGateDto, EvalMetricValueDto, EvalRunSummaryDto, EvalTagSummaryDto } from './types';

function ChangeNote({ change, className }: { change: Change | null; className?: string }) {
  if (!change) {
    return null;
  }
  if (change.kind === 'same') {
    return (
      <span data-change="same" className={clsx('inline-flex items-center gap-1 text-xs text-fg-muted', className)}>
        <ArrowRight className="size-3.5 shrink-0" aria-hidden />
        Same as last run
      </span>
    );
  }
  const better = change.kind === 'better';
  const Icon = better ? ArrowUp : ArrowDown;
  return (
    <span
      data-change={change.kind}
      className={clsx('inline-flex items-center gap-1 text-xs font-medium tabular-nums', better ? 'text-success-text' : 'text-danger-text', className)}
    >
      <Icon className="size-3.5 shrink-0" aria-hidden />
      {formatDelta(change.points)}
      <span className="font-normal text-fg-muted">{better ? 'better than last run' : 'worse than last run'}</span>
    </span>
  );
}

interface AreaCardProps {
  score: AreaScore;
  metrics: Record<string, EvalMetricValueDto>;
  missed: number;
  onShowMissed?: (area: QualityAreaId) => void;
}

function AreaCard({ score, metrics, missed, onShowMissed }: AreaCardProps) {
  const { area, value } = score;
  const verdict = VERDICTS[score.verdict];
  const info = metricInfo(score.metric);
  return (
    <Card className="flex h-full flex-col gap-3 p-4">
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-sm font-medium text-fg">{area.title}</h3>
        <Badge tone={verdict.tone} data-verdict={score.verdict}>
          {verdict.label}
        </Badge>
      </div>
      <div>
        <p className="text-kpi text-fg tabular-nums" title={`${info.label}: ${info.description}`}>
          {formatPercent(value.mean)}
        </p>
        <ChangeNote change={score.change} />
      </div>
      <p className="text-small text-fg-secondary">
        {area.meaning} {score.verdict === 'good' ? null : area.improve}
      </p>
      {score.supporting.length > 0 || area.id === 'declines' ? (
        <dl className="space-y-1 border-t border-border-subtle pt-3 text-xs">
          {area.id === 'declines' ? (
            <div className="flex justify-between gap-3">
              <dt className="text-fg-muted">Asked</dt>
              <dd className="text-fg-secondary tabular-nums">{pluralize(value.count, 'question')} it should decline</dd>
            </div>
          ) : null}
          {score.supporting.map((name) => (
            <div key={name} className="flex justify-between gap-3" title={metricInfo(name).description}>
              <dt className="text-fg-muted">{metricInfo(name).label}</dt>
              <dd className="text-fg-secondary tabular-nums">{formatPercent(metrics[name]?.mean)}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      {missed > 0 && onShowMissed ? (
        <button
          type="button"
          onClick={() => onShowMissed(area.id)}
          className="mt-auto w-fit rounded-sm text-small font-medium text-accent-text hover:underline"
        >
          Show {missed === 1 ? 'the question' : `the ${formatNumber(missed)} questions`} that fell short
        </button>
      ) : null}
    </Card>
  );
}

export interface QualityCardsProps {
  metrics: Record<string, EvalMetricValueDto>;
  /** The run the change is computed against. */
  previous: EvalRunSummaryDto | null;
  active: boolean;
  /** Questions below the Good line, per area. */
  missed?: Partial<Record<QualityAreaId, number>>;
  onShowMissed?: (area: QualityAreaId) => void;
}

export function QualityCards({ metrics, previous, active, missed = {}, onShowMissed }: QualityCardsProps) {
  const scores = scoreAreas(metrics, previous?.metrics);
  if (scores.length === 0) {
    return (
      <Card>
        <EmptyState
          icon={ChartColumn}
          title="No scores yet"
          description={active ? 'Scores appear as the questions are answered.' : 'Nothing could be scored in this run.'}
        />
      </Card>
    );
  }
  return (
    <ul className="grid gap-3 @lg:grid-cols-2 @5xl:grid-cols-4" aria-label="Quality">
      {scores.map((score) => (
        <li key={score.area.id} data-area={score.area.id} data-metric={score.metric}>
          <AreaCard score={score} metrics={metrics} missed={missed[score.area.id] ?? 0} onShowMissed={onShowMissed} />
        </li>
      ))}
    </ul>
  );
}

export function TechnicalDetails({ metrics, previous }: { metrics: Record<string, EvalMetricValueDto>; previous: EvalRunSummaryDto | null }) {
  const names = presentMetrics([metrics]);
  if (names.length === 0) {
    return null;
  }
  return (
    <CollapsibleSection
      title="Technical details"
      description={`All ${formatNumber(names.length)} measurements behind the scores above, with their usual names.`}
    >
      <div className="overflow-hidden rounded-xl border border-border">
        <Table aria-label="Technical details">
          <TableHead>
            <TableHeader>Measurement</TableHeader>
            <TableHeader numeric>Score</TableHeader>
            <TableHeader numeric>Change</TableHeader>
            <TableHeader numeric>Questions</TableHeader>
          </TableHead>
          <tbody>
            {names.map((name) => {
              const info = metricInfo(name);
              const value = metrics[name]!;
              const change = previous ? changeOf(metricDelta(metrics, previous.metrics, name)) : null;
              return (
                <TableRow key={name} data-metric={name}>
                  <TableRowHeader>
                    <span title={info.description} className="cursor-help underline decoration-border-strong decoration-dotted underline-offset-4">
                      {info.technical}
                    </span>
                    <span className="block text-xs font-normal text-fg-muted">{info.label}</span>
                    <span className="sr-only">. {info.description}</span>
                  </TableRowHeader>
                  <TableCell numeric>{formatPercent(value.mean)}</TableCell>
                  <TableCell numeric className="whitespace-nowrap">
                    {change === null ? (
                      <span className="text-fg-muted">–</span>
                    ) : change.kind === 'same' ? (
                      <span className="text-fg-muted">Same</span>
                    ) : (
                      <span className={change.kind === 'better' ? 'text-success-text' : 'text-danger-text'}>{formatDelta(change.points)}</span>
                    )}
                  </TableCell>
                  <TableCell numeric>{formatNumber(value.count)}</TableCell>
                </TableRow>
              );
            })}
          </tbody>
        </Table>
      </div>
    </CollapsibleSection>
  );
}

export function GateChecks({ gate }: { gate: EvalGateDto }) {
  return (
    <Card className="overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 pt-4 pb-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h2 id="run-gate-heading" className="text-section text-fg">
              Quality gate
            </h2>
            <GateBadge gate={gate} />
          </div>
          <p className="mt-0.5 text-small text-fg-secondary">The scores a run must reach before you ship a change.</p>
        </div>
        <p className="min-w-0 text-xs break-all text-fg-muted">
          {gate.baselineSignature ? (
            <>
              Baseline <code className="font-mono">{gate.baselineSignature}</code>
            </>
          ) : (
            'No baseline yet: only the lowest allowed scores apply.'
          )}
        </p>
      </div>
      {gate.checks.length === 0 ? (
        <p className="border-t border-border-subtle px-5 py-3 text-sm text-fg-muted">No checks are configured.</p>
      ) : (
        <ul className="divide-y divide-border-subtle border-t border-border-subtle" aria-label="Gate checks">
          {gate.checks.map((check, index) => {
            const info = metricInfo(check.metric);
            const values = describeGateCheck(check);
            return (
              <li key={`${check.metric}:${check.kind}:${index}`} data-passed={check.passed} className="flex items-start gap-3 px-5 py-3">
                {check.passed ? (
                  <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
                ) : (
                  <CircleX className="mt-0.5 size-4 shrink-0 text-danger" aria-hidden />
                )}
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
                    <span className="sr-only">{check.passed ? 'Passed:' : 'Failed:'}</span>
                    <span className="text-sm font-medium text-fg" title={info.description || undefined}>
                      {info.label}
                    </span>
                    {check.kind ? <Badge>{gateCheckKindLabel(check.kind)}</Badge> : null}
                    {values ? <span className="text-xs text-fg-muted tabular-nums">{values}</span> : null}
                  </div>
                  {/* The API message uses technical names; only needed when the values can't be shown. */}
                  {!values && check.message ? <p className="mt-0.5 text-small break-words text-fg-secondary">{check.message}</p> : null}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

export interface TagTableProps {
  byTag: EvalTagSummaryDto[];
  overall: Record<string, EvalMetricValueDto>;
  totalItems: number;
}

export function TagTable({ byTag, overall, totalItems }: TagTableProps) {
  const rows = QUALITY_AREAS.flatMap((area) => {
    const metric = headlineMetric(area, overall) ?? byTag.map((tag) => headlineMetric(area, tag.metrics)).find(Boolean);
    return metric && (overall[metric] || byTag.some((tag) => tag.metrics[metric])) ? [{ area, metric }] : [];
  });
  return (
    <Table aria-label="Scores by tag">
      <TableHead>
        <TableHeader className="py-2">Question</TableHeader>
        <TableHeader numeric className="py-2">
          All
          <span className="block font-normal">{pluralize(totalItems, 'question')}</span>
        </TableHeader>
        {byTag.map((tag) => (
          <TableHeader key={tag.tag} numeric className="py-2">
            <span className="font-mono">{tag.tag}</span>
            <span className="block font-normal">{pluralize(tag.items, 'question')}</span>
          </TableHeader>
        ))}
      </TableHead>
      <tbody>
        {rows.map(({ area, metric }) => (
          <TableRow key={area.id}>
            <TableRowHeader className="whitespace-nowrap" title={metricInfo(metric).description || undefined}>
              {area.title}
            </TableRowHeader>
            <TableCell numeric>{formatPercent(overall[metric]?.mean)}</TableCell>
            {byTag.map((tag) => {
              const value = tag.metrics[metric];
              return (
                <TableCell key={tag.tag} numeric title={value ? `${pluralize(value.count, 'question')} measured` : undefined}>
                  {value ? <span className="text-fg">{formatPercent(value.mean)}</span> : <span className="text-fg-muted">–</span>}
                </TableCell>
              );
            })}
          </TableRow>
        ))}
      </tbody>
    </Table>
  );
}
