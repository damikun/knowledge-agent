import { clsx } from 'clsx';
import { ClipboardPlus, MessagesSquare } from 'lucide-react';
import { useEffectEvent, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState } from '../../components/ui/feedback';
import { formatDateTime, formatRelativeTime } from '../../lib/format';
import { AssistantMessage } from '../chat/AssistantMessage';
import { citationIndex } from '../chat/chatReducer';
import { isQuestion } from '../chat/timeline';
import type { ConversationMessageDto } from '../chat/types';
import type { DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { FEEDBACK_ICON_TONES, FeedbackNote, feedbackLabel } from './ConversationDetails';
import { MISSED_REQUEST, type InboxItem } from './inboxTimeline';
import type { ConversationFeedbackItem, ConversationHistory } from './types';

function Time({ value }: { value: string }) {
  return (
    <time dateTime={value} title={formatDateTime(value)} className="text-xs text-fg-muted">
      {formatRelativeTime(value)}
    </time>
  );
}

function Author({ name, time, ai = false, children }: { name: string; time?: string; ai?: boolean; children?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-xs font-medium text-fg-secondary">{name}</span>
      {ai ? <Badge title="Answered by the assistant">AI</Badge> : null}
      {time ? <Time value={time} /> : null}
      {children}
    </div>
  );
}

function Bubble({ tone, children }: { tone: 'customer' | 'staff'; children: ReactNode }) {
  return (
    <div
      className={clsx(
        'max-w-[85%] rounded-2xl px-3.5 py-2 text-sm leading-[1.375rem] break-words whitespace-pre-wrap text-fg',
        tone === 'staff' ? 'bg-accent-subtle' : 'bg-fill-muted',
      )}
    >
      {children}
    </div>
  );
}

function SystemEvent({ children, time }: { children: ReactNode; time: string }) {
  return (
    <p className="mx-auto max-w-md text-center text-xs text-fg-muted">
      {children} <Time value={time} />
    </p>
  );
}

/** The last question asked before the answer. */
function questionBefore(messages: readonly ConversationMessageDto[], answer: ConversationMessageDto): string {
  return [...messages].reverse().find((message) => message.sequence < answer.sequence && isQuestion(message))?.text ?? '';
}

type Entry = { kind: 'item'; item: InboxItem } | { kind: 'event'; key: string; at: string; node: ReactNode };

// Ratings and missed requests aren't messages, so they're merged in by time.
function historyEvents(history: ConversationHistory | undefined): Array<Extract<Entry, { kind: 'event' }>> {
  if (!history) {
    return [];
  }
  const ratings = history.feedback
    .filter((item) => item.kind !== 'answer')
    .map((item) => {
      const { icon: Icon, text, tone } = feedbackLabel(item);
      return {
        kind: 'event' as const,
        key: `feedback-${item.id}`,
        at: item.createdAt,
        node: (
          <div className="mx-auto max-w-md space-y-1 text-center">
            <p className="inline-flex flex-wrap items-center justify-center gap-1.5 text-xs text-fg-secondary">
              <Icon className={clsx('size-3.5', FEEDBACK_ICON_TONES[tone])} aria-hidden />
              <span>
                {item.author === 'staff' ? 'Staff' : 'The visitor'} {text.charAt(0).toLowerCase() + text.slice(1)}
              </span>
              <Time value={item.createdAt} />
            </p>
            <FeedbackNote item={item} />
          </div>
        ),
      };
    });
  const missed = history.handovers
    .filter((handover) => handover.status === 'expired' && handover.resolvedAt)
    .map((handover) => ({
      kind: 'event' as const,
      key: `missed-${handover.id}`,
      at: handover.resolvedAt!,
      node: (
        <SystemEvent time={handover.resolvedAt!}>
          <span className="font-medium text-danger-text">{MISSED_REQUEST}:</span> nobody joined in time.
        </SystemEvent>
      ),
    }));
  return [...ratings, ...missed].sort((a, b) => a.at.localeCompare(b.at));
}

function interleave(items: readonly InboxItem[], events: ReadonlyArray<Extract<Entry, { kind: 'event' }>>): Entry[] {
  const entries: Entry[] = [];
  let next = 0;
  for (const item of items) {
    if (item.kind === 'message') {
      while (next < events.length && events[next]!.at < item.message.createdAt) {
        entries.push(events[next++]!);
      }
    }
    entries.push({ kind: 'item', item });
  }
  return [...entries, ...events.slice(next)];
}

interface MessageItemProps {
  item: InboxItem;
  citations: ReturnType<typeof citationIndex>;
  assistantName: string;
  rating: ConversationFeedbackItem | undefined;
  onAddToDataset: (() => void) | null;
}

function MessageItem({ item, citations, assistantName, rating, onAddToDataset }: MessageItemProps) {
  if (item.kind === 'turn') {
    return (
      <div className="space-y-1.5">
        <Author name={assistantName} ai />
        <AssistantMessage turn={item.turn} conversationCitations={citations} />
      </div>
    );
  }
  const { message } = item;
  switch (message.author) {
    case 'system':
      return <SystemEvent time={message.createdAt}>{message.text}</SystemEvent>;
    case 'assistant':
      return (
        <div className="space-y-1.5">
          {/* Staff see which specialist answered, whatever name the visitor saw. */}
          <Author name={message.authorName ?? assistantName} time={message.createdAt} ai>
            {rating ? <Badge tone={rating.rating > 0 ? 'success' : 'danger'}>{rating.rating > 0 ? 'Rated helpful' : 'Rated not helpful'}</Badge> : null}
          </Author>
          <AssistantMessage turn={item.turn!} conversationCitations={citations} />
          {rating ? <FeedbackNote item={rating} /> : null}
          {onAddToDataset ? (
            <Button size="sm" variant="ghost" icon={ClipboardPlus} onClick={onAddToDataset} className="-ml-2">
              Add to dataset
            </Button>
          ) : null}
        </div>
      );
    case 'staff':
      return (
        <div className="flex flex-col items-end gap-1">
          <Author name={`${message.authorName ?? 'Staff'} (staff)`} time={message.createdAt} />
          <Bubble tone="staff">{message.text}</Bubble>
        </div>
      );
    default:
      return (
        <div className="flex flex-col items-start gap-1">
          <Author name={message.authorName ?? (message.author === 'user' ? 'Portal user' : 'Visitor')} time={message.createdAt} />
          <Bubble tone="customer">{message.text}</Bubble>
        </div>
      );
  }
}

export interface ConversationThreadProps {
  items: InboxItem[];
  messages: readonly ConversationMessageDto[];
  history: ConversationHistory | undefined;
  assistantName: string;
  conversationId: string;
  onAddToDataset: ((source: DatasetQuestionSource) => void) | null;
}

export function ConversationThread({ items, messages, history, assistantName, conversationId, onAddToDataset }: ConversationThreadProps) {
  const turns = items.flatMap((item) => (item.turn ? [item.turn] : []));
  const finished = turns.filter((turn) => turn.status !== 'streaming');
  const finishedKey = finished.map((turn) => `${turn.key}:${turn.citations.length}`).join('|');
  // Keyed on finished answers, so earlier answers don't re-render on every streamed token.
  const citations = useMemo(() => citationIndex(finished), [finishedKey]);
  const ratings = useMemo(
    () => new Map((history?.feedback ?? []).filter((item) => item.kind === 'answer' && item.messageId).map((item) => [item.messageId!, item] as const)),
    [history],
  );
  const entries = useMemo(() => interleave(items, historyEvents(history)), [items, history]);

  if (items.length === 0) {
    return <EmptyState icon={MessagesSquare} title="No messages yet" className="h-full" />;
  }
  return (
    <FollowingScroll changes={entries}>
      <ol className="mx-auto max-w-3xl space-y-5 px-4 py-5" aria-label="Messages">
        {entries.map((entry) => {
          if (entry.kind === 'event') {
            return <li key={entry.key}>{entry.node}</li>;
          }
          const { item } = entry;
          const answer = item.kind === 'message' && item.message.author === 'assistant' ? item.message : null;
          return (
            <li key={item.kind === 'turn' ? `turn-${item.turn.key}` : item.message.id}>
              <MessageItem
                item={item}
                citations={citations}
                assistantName={assistantName}
                rating={answer ? ratings.get(answer.id) : undefined}
                onAddToDataset={
                  answer && onAddToDataset
                    ? () => onAddToDataset({ question: questionBefore(messages, answer), answer: answer.text, conversationId, messageId: answer.id })
                    : null
                }
              />
            </li>
          );
        })}
      </ol>
    </FollowingScroll>
  );
}

/**
 * Follows new entries while scrolled to the end and stays put once scrolled up. Holds that state itself so
 * scrolling re-renders only this wrapper, not the messages.
 */
function FollowingScroll({ changes, children }: { changes: unknown; children: ReactNode }) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [following, setFollowing] = useState(true);
  const isFollowing = useEffectEvent(() => following);

  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (el && isFollowing()) {
      el.scrollTop = el.scrollHeight;
    }
  }, [changes]);

  return (
    <div
      ref={scrollRef}
      className="h-full overflow-y-auto"
      onScroll={(event) => {
        const el = event.currentTarget;
        setFollowing(el.scrollHeight - el.scrollTop - el.clientHeight < 80);
      }}
    >
      {children}
    </div>
  );
}
