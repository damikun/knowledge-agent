import { clsx } from 'clsx';
import { CircleCheck, CircleSlash, CircleX, LoaderCircle, ShieldCheck, ShieldX } from 'lucide-react';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { formatNumber, pluralize } from '../../lib/format';
import { isRunActive, normalizeRunStatus } from './polling';
import { DATASET_SOURCE_LABELS } from './runs';
import type { EvalGateDto, EvalRunStatus, EvalRunSummaryDto } from './types';

const STATUS: Record<EvalRunStatus, { label: string; tone: BadgeTone; description: string }> = {
  queued: { label: 'Queued', tone: 'neutral', description: 'Waiting to start' },
  running: { label: 'Running', tone: 'accent', description: 'Asking the questions and scoring the answers' },
  completed: { label: 'Completed', tone: 'success', description: 'Every question was asked and scored' },
  failed: { label: 'Failed', tone: 'danger', description: 'The run stopped with an error' },
  cancelled: { label: 'Cancelled', tone: 'neutral', description: 'Cancelled before it finished' },
};

type RunProgressFields = Pick<EvalRunSummaryDto, 'status' | 'completedItems' | 'totalItems' | 'failedItems' | 'error'>;

export function RunStatusBadge({ run }: { run: Pick<EvalRunSummaryDto, 'status' | 'error'> }) {
  const status = normalizeRunStatus(run.status);
  if (!status) {
    return <Badge>{run.status || 'Unknown'}</Badge>;
  }
  const info = STATUS[status];
  return (
    <Badge
      tone={info.tone}
      title={(status === 'failed' || status === 'cancelled') && run.error ? run.error : info.description}
      data-status={status}
    >
      {status === 'queued' ? (
        <span className="size-1.5 animate-pulse rounded-full bg-current" aria-hidden />
      ) : status === 'running' ? (
        <LoaderCircle className="size-3 animate-spin" aria-hidden />
      ) : status === 'completed' ? (
        <CircleCheck className="size-3" aria-hidden />
      ) : status === 'failed' ? (
        <CircleX className="size-3" aria-hidden />
      ) : (
        <CircleSlash className="size-3" aria-hidden />
      )}
      {info.label}
    </Badge>
  );
}

export function RunProgress({ run, className }: { run: RunProgressFields; className?: string }) {
  const active = isRunActive(run.status);
  const total = Math.max(0, run.totalItems);
  const done = Math.min(total, Math.max(0, run.completedItems));
  if (!(active && total > 0) && run.failedItems <= 0) {
    return null;
  }
  return (
    <div className={clsx('space-y-0.5 text-xs text-fg-muted', className)}>
      {active && total > 0 ? (
        <div className="flex items-center gap-2">
          <span
            role="progressbar"
            aria-label="Questions answered"
            aria-valuemin={0}
            aria-valuemax={total}
            aria-valuenow={done}
            className="relative h-1 w-16 overflow-hidden rounded-full bg-fill-muted"
          >
            <span className="absolute inset-y-0 left-0 rounded-full bg-accent transition-[width]" style={{ width: `${(done / total) * 100}%` }} />
          </span>
          <span className="tabular-nums">
            {formatNumber(done)}/{formatNumber(total)}
          </span>
        </div>
      ) : null}
      {run.failedItems > 0 ? <div className="text-danger-text">{pluralize(run.failedItems, 'failed question')}</div> : null}
    </div>
  );
}

export function GateBadge({ gate }: { gate: EvalGateDto | null }) {
  if (!gate) {
    return (
      <span className="text-fg-muted" title="No quality gate was evaluated">
        <span aria-hidden>–</span>
        <span className="sr-only">No gate</span>
      </span>
    );
  }
  const failed = gate.checks.filter((check) => !check.passed).length;
  return gate.passed ? (
    <Badge tone="success" title={gate.checks.length > 0 ? `${pluralize(gate.checks.length, 'check')} passed` : 'Passed'} data-gate="passed">
      <ShieldCheck className="size-3" aria-hidden />
      Passed
    </Badge>
  ) : (
    <Badge tone="danger" title={`${formatNumber(failed)} of ${pluralize(gate.checks.length, 'check')} failed`} data-gate="failed">
      <ShieldX className="size-3" aria-hidden />
      Failed
    </Badge>
  );
}

export function DatasetSourceBadge({ source }: { source: string }) {
  const info = DATASET_SOURCE_LABELS[source?.toLowerCase()];
  if (!info) {
    return <Badge>{source || 'Unknown'}</Badge>;
  }
  return (
    <Badge tone={source.toLowerCase() === 'langfuse' ? 'accent' : 'neutral'} title={info.description}>
      {info.label}
    </Badge>
  );
}
