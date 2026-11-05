import { clsx } from 'clsx';
import { Mail, MailOpen, Pin, PinOff, Star, ThumbsDown, UserRoundCheck, UserRoundX } from 'lucide-react';
import { memo } from 'react';
import { Link } from 'react-router';
import { Avatar } from '../../../components/ui/Avatar';
import { Badge } from '../../../components/ui/Badge';
import { Menu } from '../../../components/ui/Menu';
import { StatusDot } from '../../../components/ui/StatusDot';
import type { Tone } from '../../../components/ui/tone';
import { formatDateTime, formatRelativeTime } from '../../../lib/format';
import { useKeptReturnState } from '../../../lib/returnTo';
import type { ConversationStatus } from '../../chat/types';
import { LabelChips } from '../../labels/LabelChip';
import { MISSED_REQUEST } from '../inboxTimeline';
import { STATUS_LABELS, widgetLabel } from '../status';
import type { ConversationSearchItem } from '../types';

const STATUS_DOTS: Readonly<Record<ConversationStatus, Tone>> = {
  handover_pending: 'warning',
  human: 'info',
  ai: 'neutral',
  closed: 'neutral',
};

function Highlighted({ text, words }: { text: string; words: readonly string[] }) {
  if (words.length === 0) {
    return <>{text}</>;
  }
  const pattern = new RegExp(`(${words.map((word) => word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'gi');
  return (
    <>
      {text.split(pattern).map((part, index) =>
        index % 2 === 1 ? (
          <mark key={index} className="rounded-sm bg-warning-subtle px-0.5 text-fg">
            {part}
          </mark>
        ) : (
          part
        ),
      )}
    </>
  );
}

interface RowProps {
  item: ConversationSearchItem;
  words: readonly string[];
  active: boolean;
  /** A string path rather than an object, so memoized rows stay equal across refreshes. */
  to: string;
  widgetNames: ReadonlyMap<string, string> | undefined;
  byCreated: boolean;
}

/** Memoized: the list refreshes every few seconds and only changed rows should render. */
export const ConversationRow = memo(function ConversationRow({ item, words, active, to, widgetNames, byCreated }: RowProps) {
  const kept = useKeptReturnState();
  const waiting = item.status === 'handover_pending';
  const strong = waiting || item.unread;
  const when = byCreated ? item.createdAt : item.updatedAt;
  const snippet = item.match ?? item.preview;
  return (
    <Link
      to={to}
      state={kept}
      aria-current={active ? 'page' : undefined}
      className={clsx('block rounded-lg px-3 py-3', active ? 'bg-fill-active' : 'hover:bg-fill-hover')}
    >
      <span className="flex items-center gap-2">
        <StatusDot tone={STATUS_DOTS[item.status]} />
        {item.unread ? <span className="sr-only">Unread: </span> : null}
        {item.pinned ? (
          <>
            <Pin className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
            <span className="sr-only">Pinned: </span>
          </>
        ) : null}
        <span className={clsx('min-w-0 flex-1 truncate text-sm text-fg', strong ? 'font-semibold' : 'font-medium')}>
          <Highlighted text={item.title || 'New conversation'} words={words} />
        </span>
        {item.unread ? <span className="size-2 shrink-0 rounded-full bg-accent" aria-hidden title="Unread" /> : null}
        <time dateTime={when} title={formatDateTime(when)} className={clsx('shrink-0 text-xs tabular-nums', waiting ? 'font-medium text-warning-text' : 'text-fg-muted')}>
          {formatRelativeTime(when)}
        </time>
      </span>
      {snippet ? (
        <span className={clsx('mt-0.5 block truncate pl-4 text-small', item.unread ? 'text-fg' : 'text-fg-secondary')}>
          <Highlighted text={snippet} words={item.match ? words : []} />
        </span>
      ) : null}
      <span className="mt-1.5 flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 pl-4 text-xs text-fg-muted">
        {waiting ? (
          <Badge tone="warning" dot>
            {STATUS_LABELS.handover_pending}
          </Badge>
        ) : (
          <span>{STATUS_LABELS[item.status]}</span>
        )}
        {item.handoverStatus === 'expired' ? (
          <Badge tone="danger" dot>
            {MISSED_REQUEST}
          </Badge>
        ) : null}
        {item.contact ? <span className="min-w-0 truncate font-medium text-fg-secondary">{item.contact.name}</span> : null}
        <span className="min-w-0 truncate">{item.channel === 'widget' ? widgetLabel(item.widgetId, widgetNames) : 'Playground test'}</span>
        {item.rating !== null ? (
          <span className={clsx('inline-flex items-center gap-0.5', item.rating <= 2 && 'text-danger-text')} title={`Chat rated ${item.rating} of 5`}>
            <Star className="size-3" aria-hidden />
            <span className="tabular-nums">{item.rating}</span>
            <span className="sr-only"> of 5</span>
          </span>
        ) : null}
        {item.thumbsDown > 0 ? (
          <span className="inline-flex items-center gap-0.5 text-danger-text" title={`${item.thumbsDown} answers rated not helpful`}>
            <ThumbsDown className="size-3" aria-hidden />
            <span className="tabular-nums">{item.thumbsDown}</span>
            <span className="sr-only"> answers rated not helpful</span>
          </span>
        ) : null}
      </span>
      <span className="mt-1.5 flex items-center gap-2 pl-4">
        <LabelChips labels={item.labels} max={2} className="min-w-0 flex-1" />
        {item.assignee ? (
          <span className="ml-auto flex shrink-0 items-center gap-1 text-xs text-fg-muted" title={`Assigned to ${item.assignee.name}`}>
            <Avatar name={item.assignee.name} size="sm" className="size-5! text-[10px]" />
            <span className="sr-only">Assigned to {item.assignee.name}</span>
          </span>
        ) : null}
      </span>
    </Link>
  );
});

export interface RowActionsProps {
  item: ConversationSearchItem;
  me: { id: string; name: string } | null;
  pin: (variables: { id: string; pinned: boolean }) => void;
  setUnread: (variables: { id: string; unread: boolean }) => void;
  assign: (variables: { id: string; assignee: { id: string; name: string } | null }) => void;
}

/** Beside the link rather than inside it, so the row stays a single link. */
export const RowActions = memo(function RowActions({ item, me, pin, setUnread, assign }: RowActionsProps) {
  return (
    <span className="absolute right-2 bottom-2 flex items-center rounded-md bg-surface opacity-0 group-hover:opacity-100 focus-within:opacity-100 has-[[aria-expanded=true]]:opacity-100">
      <Menu
        label={`Actions for ${item.title || 'New conversation'}`}
        items={[
          item.pinned
            ? { label: 'Unpin', icon: PinOff, onSelect: () => pin({ id: item.id, pinned: false }) }
            : { label: 'Pin', icon: Pin, onSelect: () => pin({ id: item.id, pinned: true }) },
          item.unread
            ? { label: 'Mark as read', icon: MailOpen, onSelect: () => setUnread({ id: item.id, unread: false }) }
            : { label: 'Mark as unread', icon: Mail, onSelect: () => setUnread({ id: item.id, unread: true }) },
          ...(me
            ? [
                item.assignee?.id === me.id
                  ? { label: 'Unassign', icon: UserRoundX, onSelect: () => assign({ id: item.id, assignee: null }) }
                  : { label: 'Assign to me', icon: UserRoundCheck, onSelect: () => assign({ id: item.id, assignee: me }) },
              ]
            : []),
        ]}
      />
    </span>
  );
});
