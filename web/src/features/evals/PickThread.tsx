import { Check, MessageSquare } from 'lucide-react';
import { useId } from 'react';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Switch } from '../../components/ui/Switch';
import { formatRelativeTime, pluralize } from '../../lib/format';
import { useConversation } from '../chat/api';
import { isQuestion } from '../chat/timeline';
import type { ConversationMessageDto } from '../chat/types';
import { duplicateKey } from './questionUpload';
import type { EvalTurn } from './types';

const MAX_CONTEXT_TURNS = 20;

export interface ConversationPick {
  key: string;
  conversationId: string;
  conversationTitle: string;
  question: string;
  answer: string;
  answerMessageId: string | null;
  /** Sent as history when `withContext` is on. */
  context: EvalTurn[];
  withContext: boolean;
}

function repliesTo(messages: readonly ConversationMessageDto[], index: number): ConversationMessageDto[] {
  const replies: ConversationMessageDto[] = [];
  for (const message of messages.slice(index + 1)) {
    if (isQuestion(message)) {
      break;
    }
    if (message.author === 'assistant' || message.author === 'staff') {
      replies.push(message);
    }
  }
  return replies;
}

function defaultReply(replies: readonly ConversationMessageDto[]): ConversationMessageDto | null {
  return [...replies].reverse().find((reply) => reply.author === 'staff') ?? replies[0] ?? null;
}

function contextBefore(messages: readonly ConversationMessageDto[], index: number): EvalTurn[] {
  return messages
    .slice(0, index)
    .filter((message) => isQuestion(message) || message.author === 'assistant' || message.author === 'staff')
    .map((message): EvalTurn => ({ role: isQuestion(message) ? 'user' : 'assistant', content: message.text }))
    .filter((turn) => turn.content.trim())
    .slice(-MAX_CONTEXT_TURNS);
}

const AUTHOR_LABELS: Partial<Record<ConversationMessageDto['author'], string>> = { visitor: 'Visitor', user: 'Visitor', assistant: 'Agent', staff: 'Person' };

interface ThreadProps {
  conversationId: string;
  picks: ReadonlyMap<string, ConversationPick>;
  existing: ReadonlySet<string>;
  onPick: (pick: ConversationPick) => void;
  onUnpick: (key: string) => void;
  onChange: (pick: ConversationPick) => void;
}

export function PickThread({ conversationId, picks, existing, onPick, onUnpick, onChange }: ThreadProps) {
  const idPrefix = useId();
  const conversation = useConversation(conversationId);
  if (conversation.isPending) {
    return <Spinner label="Loading the conversation" />;
  }
  if (conversation.isError) {
    return <ErrorMessage error={conversation.error} title="Could not load the conversation" />;
  }
  const messages = conversation.data.messages.filter((message) => message.author !== 'system' && message.text.trim());
  if (messages.length === 0) {
    return <EmptyState icon={MessageSquare} title="No messages" description="This conversation has nothing to pick yet." />;
  }
  return (
    <ol className="space-y-3" aria-label="Messages">
      {messages.map((message, index) => {
        const question = isQuestion(message);
        const pick = picks.get(message.id);
        const already = question && existing.has(duplicateKey(message.text));
        return (
          <li key={message.id} className={question ? 'rounded-xl bg-fill-muted p-3' : 'px-3'}>
            <div className="flex items-baseline justify-between gap-2">
              <span className="text-xs font-medium text-fg-muted">{AUTHOR_LABELS[message.author] ?? message.authorName ?? 'Message'}</span>
              <span className="text-xs text-fg-muted">{formatRelativeTime(message.createdAt)}</span>
            </div>
            {pick ? null : <p className="mt-1 line-clamp-6 text-sm whitespace-pre-wrap text-fg">{message.text}</p>}
            {question ? (
              already ? (
                <Badge className="mt-2">Already in the dataset</Badge>
              ) : pick ? (
                <div className="mt-2 space-y-3">
                  <label className="block space-y-1">
                    <span className="text-small font-medium text-fg">Question</span>
                    <textarea
                      rows={2}
                      value={pick.question}
                      onChange={(event) => onChange({ ...pick, question: event.target.value })}
                      className="field min-h-14 py-1.5"
                    />
                  </label>
                  <label className="block space-y-1">
                    <span className="text-small font-medium text-fg">Expected answer</span>
                    <textarea
                      rows={4}
                      value={pick.answer}
                      onChange={(event) => onChange({ ...pick, answer: event.target.value })}
                      className="field min-h-20 py-1.5"
                      aria-describedby={`${idPrefix}-${message.id}-answer-hint`}
                    />
                    <span id={`${idPrefix}-${message.id}-answer-hint`} className="block text-xs text-fg-muted">
                      The reply it got. Correct it here if it was wrong.
                    </span>
                  </label>
                  {pick.context.length > 0 ? (
                    <Switch
                      checked={pick.withContext}
                      onChange={(withContext) => onChange({ ...pick, withContext })}
                      label={`Ask it as a follow-up, after the ${pluralize(pick.context.length, 'earlier turn')}`}
                    />
                  ) : null}
                  <Button size="sm" variant="ghost" onClick={() => onUnpick(message.id)}>
                    Leave this question out
                  </Button>
                </div>
              ) : (
                <Button
                  size="sm"
                  className="mt-2"
                  icon={Check}
                  onClick={() => {
                    const reply = defaultReply(repliesTo(messages, index));
                    onPick({
                      key: message.id,
                      conversationId,
                      conversationTitle: conversation.data.title,
                      question: message.text,
                      answer: reply?.text ?? '',
                      answerMessageId: reply?.id ?? null,
                      context: contextBefore(messages, index),
                      withContext: false,
                    });
                  }}
                >
                  Use this question
                </Button>
              )
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}
