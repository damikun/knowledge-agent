import type { ReactNode } from 'react';
import { Badge, type BadgeTone } from '../../components/ui/Badge';
import { Alert } from '../../components/ui/feedback';
import { formatDateTime, formatRelativeTime } from '../../lib/format';
import { MISSED_REQUEST } from './inboxTimeline';
import type { HandoverDto, HandoverSource, HandoverStatus } from './types';

const STATUS: Readonly<Record<HandoverStatus, { label: string; tone: BadgeTone }>> = {
  pending: { label: 'Waiting', tone: 'warning' },
  active: { label: 'Taken', tone: 'accent' },
  resolved: { label: 'Resolved', tone: 'neutral' },
  cancelled: { label: 'Cancelled', tone: 'neutral' },
  expired: { label: MISSED_REQUEST, tone: 'danger' },
};

const SOURCE: Readonly<Record<HandoverSource, string>> = {
  agent: 'The assistant asked for a person',
  visitor: 'The visitor asked for a person',
  staff: 'Staff joined the conversation',
};

const CHANNEL: Readonly<Record<string, string>> = {
  portal: 'Inbox only',
  email: 'Sent by email',
  slack: 'Posted to Slack',
};

function Time({ value }: { value: string | null }) {
  return value ? (
    <time dateTime={value} title={formatDateTime(value)}>
      {formatRelativeTime(value)}
    </time>
  ) : null;
}

export function DetailRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-fg-muted">{label}</dt>
      <dd className="min-w-0 break-words text-fg">{children}</dd>
    </>
  );
}

export function HandoverPanel({ handover }: { handover: HandoverDto }) {
  const status = STATUS[handover.status];
  return (
    <section aria-labelledby={`handover-${handover.id}`} className="space-y-3 border-t border-border-subtle pt-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id={`handover-${handover.id}`} className="text-sm font-semibold text-fg">
          Request for a person
        </h3>
        <Badge tone={status.tone} dot>
          {status.label}
        </Badge>
      </div>
      <p className="text-small text-fg-secondary">
        {SOURCE[handover.source]}, <Time value={handover.requestedAt} />
      </p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-small">
        <DetailRow label="Sent to">{CHANNEL[handover.channel] ?? handover.channel}</DetailRow>
        <DetailRow label="Taken by">
          {handover.assignee ?? <span className="text-fg-muted">Nobody yet</span>}
          {handover.acceptedAt ? (
            <span className="text-fg-muted">
              {' '}
              (<Time value={handover.acceptedAt} />)
            </span>
          ) : null}
        </DetailRow>
        {handover.resolvedAt ? (
          <DetailRow label={handover.status === 'resolved' ? 'Resolved' : status.label}>
            <Time value={handover.resolvedAt} />
          </DetailRow>
        ) : null}
        {handover.reason ? <DetailRow label="Reason">{handover.reason}</DetailRow> : null}
      </dl>
      {handover.status === 'expired' ? (
        <p className="text-small text-fg-secondary">Nobody joined in time. A reply still reaches the visitor, by email if they left an address.</p>
      ) : null}
      {handover.status === 'cancelled' ? <p className="text-small text-fg-secondary">The visitor went back to the assistant before anyone joined.</p> : null}
      {handover.summary ? (
        <div>
          <div className="text-xs font-medium text-fg-muted">Summary</div>
          <p className="mt-1 text-small whitespace-pre-line text-fg">{handover.summary}</p>
        </div>
      ) : null}
      {handover.lastError ? (
        <Alert tone="warning" title="Sending the request failed">
          {handover.lastError}
        </Alert>
      ) : null}
    </section>
  );
}
