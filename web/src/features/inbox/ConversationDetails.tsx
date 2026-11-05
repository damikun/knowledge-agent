import { clsx } from 'clsx';
import { BadgeCheck, MessagesSquare, Star, ThumbsDown, ThumbsUp, UserRound } from 'lucide-react';
import { Link } from 'react-router';
import { Avatar } from '../../components/ui/Avatar';
import { Badge } from '../../components/ui/Badge';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { formatDateTime, formatRelativeTime } from '../../lib/format';
import { useKeptReturnState, useReturnState } from '../../lib/returnTo';
import { isAdmin, useMe } from '../auth/api';
import { REASON_LABELS } from '../feedback/labels';
import { labelsAvailable, useLabels } from '../labels/api';
import { LabelChips } from '../labels/LabelChip';
import { LabelPicker } from '../labels/LabelPicker';
import { personConversationsPath, personPath } from '../people/paths';
import type { ConversationContact } from '../people/types';
import { DetailRow, HandoverPanel } from './HandoverPanel';
import { widgetLabel } from './status';
import type { ConversationFeedbackItem, ConversationHistory, HandoverDto, InboxConversationDetail } from './types';

function Time({ value }: { value: string }) {
  return (
    <time dateTime={value} title={formatDateTime(value)} className="text-xs text-fg-muted">
      {formatRelativeTime(value)}
    </time>
  );
}

export function feedbackLabel(item: ConversationFeedbackItem): { icon: typeof Star; text: string; tone: 'success' | 'danger' | 'neutral' } {
  if (item.kind === 'conversation') {
    return { icon: Star, text: `Rated the chat ${item.rating} of 5`, tone: item.rating >= 4 ? 'success' : item.rating <= 2 ? 'danger' : 'neutral' };
  }
  if (item.kind === 'agent') {
    return { icon: MessagesSquare, text: 'Told the assistant', tone: item.rating < 0 ? 'danger' : item.rating > 0 ? 'success' : 'neutral' };
  }
  return item.rating > 0
    ? { icon: ThumbsUp, text: 'Answer was helpful', tone: 'success' }
    : { icon: ThumbsDown, text: 'Answer was not helpful', tone: 'danger' };
}

export const FEEDBACK_ICON_TONES = { success: 'text-success-text', danger: 'text-danger-text', neutral: 'text-fg-muted' } as const;

export function FeedbackNote({ item }: { item: ConversationFeedbackItem }) {
  if (!item.comment && item.reasons.length === 0) {
    return null;
  }
  return (
    <div className="space-y-1">
      {item.comment ? <p className="text-small break-words whitespace-pre-line text-fg-secondary">“{item.comment}”</p> : null}
      {item.reasons.length > 0 ? (
        <div className="flex flex-wrap gap-1">
          {item.reasons.map((reason) => (
            <Badge key={reason}>{REASON_LABELS[reason] ?? reason}</Badge>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function FeedbackList({ items }: { items: readonly ConversationFeedbackItem[] }) {
  return (
    <ul className="space-y-3">
      {items.map((item) => {
        const { icon: Icon, text, tone } = feedbackLabel(item);
        return (
          <li key={item.id} className="space-y-1">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-small">
              <Icon className={clsx('size-3.5', FEEDBACK_ICON_TONES[tone])} aria-hidden />
              <span className="font-medium text-fg">{text}</span>
              <span className="text-xs text-fg-muted">{item.author === 'staff' ? 'by staff' : 'by the visitor'},</span>
              <Time value={item.createdAt} />
            </div>
            <FeedbackNote item={item} />
          </li>
        );
      })}
    </ul>
  );
}

function PersonHeader({ contact, conversationId }: { contact: ConversationContact; conversationId: string }) {
  const fromInbox = useReturnState('Inbox');
  const kept = useKeptReturnState();
  return (
    <div className="space-y-2">
      <div className="flex items-center gap-3">
        <Avatar name={contact.name} size="lg" {...(contact.identified || contact.email ? {} : { icon: UserRound })} />
        <div className="min-w-0">
          <h3 className="flex min-w-0 items-center gap-1.5 text-sm font-semibold text-fg">
            <Link to={personPath(contact.id)} state={fromInbox} className="truncate hover:underline">
              {contact.name}
            </Link>
            {contact.identified ? <BadgeCheck className="size-3.5 shrink-0 text-accent-text" aria-label="Signed in on your website" /> : null}
          </h3>
          <p className="truncate text-xs text-fg-muted">{contact.email ?? (contact.identified ? 'Signed in' : 'Visitor')}</p>
        </div>
      </div>
      <Link to={personConversationsPath(contact.id, conversationId)} state={kept} className="inline-block text-small text-accent-text hover:underline">
        All conversations with this contact
      </Link>
    </div>
  );
}

export interface ConversationDetailsProps {
  conversation: InboxConversationDetail;
  visitorName: string | null;
  agentName: string | undefined;
  widgetNames: ReadonlyMap<string, string> | undefined;
  /** The latest request for a person; null when nobody asked. */
  handover: HandoverDto | null | undefined;
  handoverError: unknown;
  history: ConversationHistory | undefined;
  historyPending: boolean;
  historyError: unknown;
  className?: string;
}

export function ConversationDetails({
  conversation,
  visitorName,
  agentName,
  widgetNames,
  handover,
  handoverError,
  history,
  historyPending,
  historyError,
  className,
}: ConversationDetailsProps) {
  const fromWidget = conversation.channel === 'widget';
  const hasLabels = labelsAvailable(useLabels().data);
  const admin = isAdmin(useMe().data);
  // Only an actionable request gets a panel; finished ones already show as events in the thread.
  const open = handover && (handover.status === 'pending' || handover.status === 'active' || handover.status === 'expired') ? handover : null;
  return (
    <div className={clsx('space-y-5 bg-surface-subtle p-4', className)}>
      {fromWidget && conversation.contact ? (
        <PersonHeader contact={conversation.contact} conversationId={conversation.id} />
      ) : fromWidget ? (
        <div className="flex items-center gap-3">
          {visitorName ? <Avatar name={visitorName} size="lg" /> : <Avatar icon={UserRound} size="lg" />}
          <div className="min-w-0">
            <h3 className="truncate text-sm font-semibold text-fg">{visitorName ?? 'Anonymous visitor'}</h3>
            <p className="text-xs text-fg-muted">Visitor</p>
          </div>
        </div>
      ) : null}
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-small">
        <DetailRow label={fromWidget ? 'Widget' : 'Channel'}>{fromWidget ? widgetLabel(conversation.widgetId, widgetNames) : 'Playground test'}</DetailRow>
        {agentName ? <DetailRow label="Agent">{agentName}</DetailRow> : null}
        {conversation.activeAgent ? <DetailRow label="Answering now">{conversation.activeAgent.name}</DetailRow> : null}
        {conversation.createdBy ? <DetailRow label="Started by">{conversation.createdBy.name}</DetailRow> : null}
        <DetailRow label="Started">
          <time dateTime={conversation.createdAt} title={formatDateTime(conversation.createdAt)}>
            {formatRelativeTime(conversation.createdAt)}
          </time>
        </DetailRow>
      </dl>
      {hasLabels && history ? (
        <section aria-label="Labels" className="space-y-2">
          <h3 className="text-small font-medium text-fg">Labels</h3>
          <div className="flex flex-wrap items-center gap-2">
            {history.labels.length > 0 ? <LabelChips labels={history.labels} max={20} /> : <span className="text-small text-fg-muted">No labels</span>}
            <LabelPicker conversationId={conversation.id} value={history.labels} canCreate={admin} />
          </div>
        </section>
      ) : null}
      <section aria-label="Feedback" className="space-y-2">
        <h3 className="text-small font-medium text-fg">Feedback</h3>
        {historyPending ? (
          <Spinner label="Loading the feedback" />
        ) : historyError ? (
          <ErrorMessage error={historyError} title="Could not load the feedback" />
        ) : history && history.feedback.length > 0 ? (
          <FeedbackList items={history.feedback} />
        ) : (
          <p className="text-small text-fg-muted">Nobody rated this conversation.</p>
        )}
      </section>
      {open ? <HandoverPanel handover={open} /> : null}
      {handoverError ? <ErrorMessage error={handoverError} title="Could not load the request for a person" /> : null}
    </div>
  );
}
