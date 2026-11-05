import { clsx } from 'clsx';
import { Bell, CircleCheck } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { Button } from '../../components/ui/Button';
import { ErrorMessage } from '../../components/ui/feedback';
import { Popover } from '../../components/ui/Popover';
import { formatNumber, pluralize } from '../../lib/format';
import { isAdmin, useMe } from '../auth/api';
import { useAlerts } from './api';
import { AlertRow } from './AlertRow';
import type { AlertDto } from './types';

// Red only for critical alerts; otherwise ink, which keeps the digits at 4.5:1.
function badgeClass(alerts: readonly AlertDto[]) {
  return alerts.some((alert) => alert.severity === 'critical') ? 'bg-danger text-fg-on-solid' : 'bg-fg text-canvas';
}

export function AlertsBell() {
  const me = useMe();
  const alerts = useAlerts();
  const [open, setOpen] = useState(false);
  const anchorRef = useRef<HTMLButtonElement>(null);
  const popoverId = useId();
  const headingId = useId();

  const list = alerts.data?.alerts ?? [];
  const count = list.length;
  const label = count === 0 ? 'Alerts, nothing needs attention' : `Alerts, ${pluralize(count, 'thing')} ${count === 1 ? 'needs' : 'need'} attention`;
  const canSnooze = isAdmin(me.data);

  return (
    <>
      <span className="relative inline-flex">
        <Button
          ref={anchorRef}
          variant="ghost"
          size="icon"
          icon={Bell}
          aria-label={label}
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? popoverId : undefined}
          onClick={() => setOpen((current) => !current)}
        />
        {count > 0 ? (
          <span
            aria-hidden
            className={clsx(
              'pointer-events-none absolute -top-1 -right-1 flex h-4.5 min-w-4.5 items-center justify-center rounded-full px-1 text-[0.6875rem] leading-none font-semibold tabular-nums ring-2 ring-canvas forced-colors:border',
              badgeClass(list),
            )}
          >
            {count > 9 ? '9+' : formatNumber(count)}
          </span>
        ) : null}
      </span>
      <Popover open={open} onClose={() => setOpen(false)} anchorRef={anchorRef} label="Alerts" align="end" id={popoverId} className="w-[min(26rem,calc(100vw-2rem))]">
        <div className="flex items-baseline justify-between gap-3 border-b border-border-subtle px-4 py-3">
          <h2 id={headingId} className="text-sm font-semibold text-fg">
            Needs attention
          </h2>
          {alerts.data && alerts.data.snoozed > 0 ? <p className="text-xs text-fg-muted">{pluralize(alerts.data.snoozed, 'snoozed alert')}</p> : null}
        </div>
        <div className="max-h-[min(28rem,70vh)] overflow-y-auto">
          {alerts.isError ? (
            <ErrorMessage error={alerts.error} title="Could not load the alerts" className="m-3" />
          ) : !alerts.data ? (
            <p role="status" className="px-4 py-5 text-small text-fg-muted">
              Loading the alerts
            </p>
          ) : count === 0 ? (
            <p className="flex items-center gap-2 px-4 py-5 text-small text-fg-secondary">
              <CircleCheck className="size-4 shrink-0 text-success" aria-hidden />
              All good. Nothing needs you right now.
            </p>
          ) : (
            <ul aria-labelledby={headingId} className="divide-y divide-border-subtle">
              {list.map((alert) => (
                <li key={alert.key}>
                  <AlertRow alert={alert} canSnooze={canSnooze} snoozeStyle="inline" onNavigate={() => setOpen(false)} />
                </li>
              ))}
            </ul>
          )}
        </div>
      </Popover>
    </>
  );
}
