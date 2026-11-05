import { CircleCheck } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Card, ErrorMessage } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { formatNumber, pluralize } from '../../lib/format';
import { isAdmin, useMe } from '../auth/api';
import { useAlerts } from './api';
import { AlertRow } from './AlertRow';

export interface NeedsAttentionProps {
  action?: ReactNode;
  /** While present, the "All good" line stays hidden even without alerts. */
  children?: ReactNode;
}

function RowsSkeleton() {
  return (
    <div className="divide-y divide-border-subtle">
      <p role="status" className="sr-only">
        Loading the alerts
      </p>
      {Array.from({ length: 2 }, (_, index) => (
        <div key={index} aria-hidden className="flex items-start gap-3 px-4 py-3">
          <Skeleton shape="tile" className="size-8" />
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-4 w-2/5" />
            <Skeleton className="h-3.5 w-4/5" />
          </div>
        </div>
      ))}
    </div>
  );
}

export function NeedsAttention({ action, children }: NeedsAttentionProps) {
  const headingId = useId();
  const me = useMe();
  const alerts = useAlerts();
  const list = alerts.data?.alerts ?? [];
  const snoozed = alerts.data?.snoozed ?? 0;
  const canSnooze = isAdmin(me.data);

  return (
    <section aria-labelledby={headingId} className="space-y-3">
      <div className="flex min-h-7 flex-wrap items-center justify-between gap-x-4 gap-y-1">
        <h2 id={headingId} className="flex items-center gap-2 text-section text-fg">
          Needs attention
          {list.length > 0 ? (
            <Badge tone={list.some((alert) => alert.severity === 'critical') ? 'danger' : 'warning'}>{formatNumber(list.length)}</Badge>
          ) : null}
        </h2>
        {action}
      </div>
      <Card className="overflow-hidden">
        {alerts.isError ? (
          <ErrorMessage error={alerts.error} title="Could not load the alerts" className="m-4" />
        ) : !alerts.data ? (
          <RowsSkeleton />
        ) : list.length > 0 ? (
          <ul aria-label="Alerts" className="divide-y divide-border-subtle">
            {list.map((alert) => (
              <li key={alert.key}>
                <AlertRow alert={alert} canSnooze={canSnooze} snoozeStyle="menu" />
              </li>
            ))}
          </ul>
        ) : children ? null : (
          <p className="flex items-center gap-2 px-4 py-4 text-small text-fg-secondary">
            <CircleCheck className="size-4 shrink-0 text-success" aria-hidden />
            All good. Nothing needs you right now.
          </p>
        )}
        {children ? <div className={list.length > 0 ? 'border-t border-border-subtle' : undefined}>{children}</div> : null}
        {snoozed > 0 ? (
          <p className="border-t border-border-subtle bg-surface-subtle px-4 py-2 text-xs text-fg-muted">
            {pluralize(snoozed, 'snoozed alert')} hidden until {snoozed === 1 ? 'it changes or its' : 'they change or their'} snooze ends.
          </p>
        ) : null}
      </Card>
    </section>
  );
}
