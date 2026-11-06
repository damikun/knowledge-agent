import { clsx } from 'clsx';
import { ArrowRight, BellOff } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Menu } from '../../components/ui/Menu';
import { toast } from '../../components/ui/Toast';
import { TONES } from '../../components/ui/tone';
import { errorMessage } from '../../lib/api';
import { formatDateTime, formatRelativeTime } from '../../lib/format';
import { useSnoozeAlert } from './api';
import { SEVERITY_LABELS, SEVERITY_TONES, SNOOZE_CHOICES } from './labels';
import type { AlertDto } from './types';
import { useReturnState } from '../../lib/returnTo';

export interface AlertRowProps {
  alert: AlertDto;
  canSnooze: boolean;
  /** Inline inside a popover, where a menu's own portal would count as a click outside and close it. */
  snoozeStyle: 'menu' | 'inline';
  onNavigate?: () => void;
}

function useSnooze(alert: AlertDto) {
  const snooze = useSnoozeAlert();
  const choose = (hours: number | null, done: string) =>
    snooze.mutate(
      { key: alert.key, hours },
      {
        onSuccess: () => toast({ title: done, tone: 'success', description: alert.title }),
        onError: (error) => toast({ title: 'The alert was not snoozed', tone: 'danger', description: errorMessage(error) }),
      },
    );
  return { choose, pending: snooze.isPending };
}

function InlineSnooze({ alert }: { alert: AlertDto }) {
  const [open, setOpen] = useState(false);
  const { choose, pending } = useSnooze(alert);
  if (!open) {
    return (
      <Button variant="ghost" size="sm" icon={BellOff} loading={pending} onClick={() => setOpen(true)} aria-label={`Snooze “${alert.title}”`}>
        Snooze
      </Button>
    );
  }
  return (
    <div role="group" aria-label={`Snooze “${alert.title}”`} className="flex flex-wrap items-center gap-1">
      {SNOOZE_CHOICES.map((choice) => (
        <Button key={choice.label} variant="secondary" size="sm" disabled={pending} onClick={() => choose(choice.hours, choice.done)}>
          {choice.label}
        </Button>
      ))}
      <Button variant="ghost" size="sm" onClick={() => setOpen(false)}>
        Cancel
      </Button>
    </div>
  );
}

function MenuSnooze({ alert }: { alert: AlertDto }) {
  const { choose, pending } = useSnooze(alert);
  return (
    <Menu
      label={`Snooze “${alert.title}”`}
      items={SNOOZE_CHOICES.map((choice) => ({ label: choice.label, onSelect: () => choose(choice.hours, choice.done) }))}
      trigger={(props) => (
        <Button {...props} variant="ghost" size="sm" icon={BellOff} loading={pending} aria-label={`Snooze “${alert.title}”`}>
          Snooze
        </Button>
      )}
    />
  );
}

export function AlertRow({ alert, canSnooze, snoozeStyle, onNavigate }: AlertRowProps) {
  const returnState = useReturnState();
  const tone = TONES[SEVERITY_TONES[alert.severity]];
  const Icon = tone.Icon;
  return (
    <div className="flex items-start gap-2.5 px-4 py-3.5">
      <Icon className={clsx('mt-0.5 size-4 shrink-0', tone.icon)} aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="text-sm leading-5 font-medium text-fg">
          <span className="sr-only">{SEVERITY_LABELS[alert.severity]}: </span>
          {alert.title}
        </p>
        {alert.detail ? <p className="mt-1 text-small leading-5 break-words text-fg-secondary">{alert.detail}</p> : null}
        <div className="mt-2.5 flex flex-wrap items-center gap-x-3 gap-y-1">
          <Link
            to={alert.link}
            state={returnState}
            onClick={onNavigate}
            className="inline-flex min-h-6 items-center gap-1 rounded-sm text-small font-medium text-accent-text hover:underline"
            aria-label={`Fix “${alert.title}”`}
          >
            Fix
            <ArrowRight className="size-3.5" aria-hidden />
          </Link>
          {alert.since ? (
            <time dateTime={alert.since} title={formatDateTime(alert.since)} className="text-xs text-fg-muted tabular-nums">
              {formatRelativeTime(alert.since)}
            </time>
          ) : null}
          {canSnooze ? (
            <span className="ml-auto">{snoozeStyle === 'inline' ? <InlineSnooze alert={alert} /> : <MenuSnooze alert={alert} />}</span>
          ) : null}
        </div>
      </div>
    </div>
  );
}
