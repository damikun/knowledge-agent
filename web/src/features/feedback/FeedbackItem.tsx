import { clsx } from 'clsx';
import { ClipboardPlus, Frown, Meh, Smile, Star, ThumbsDown, ThumbsUp, type LucideIcon } from 'lucide-react';
import { Link } from 'react-router';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { TONES, type Tone } from '../../components/ui/tone';
import { formatDateTime, formatRelativeTime } from '../../lib/format';
import type { DatasetQuestionSource } from '../evals/AddToDatasetDialog';
import { REASON_LABELS } from './labels';
import type { FeedbackDto, FeedbackKind } from './types';
import { useReturnState } from '../../lib/returnTo';
import type { Names } from './FeedbackSummary';

const KIND_LABELS: Readonly<Record<FeedbackKind, string>> = {
  answer: 'Answer rating',
  conversation: 'Chat rating',
  agent: 'Noted by the assistant',
};

function ratingLook(feedback: FeedbackDto): { icon: LucideIcon; tone: Tone; label: string } {
  if (feedback.kind === 'conversation') {
    return { icon: Star, tone: feedback.rating >= 4 ? 'success' : feedback.rating <= 2 ? 'danger' : 'neutral', label: `${feedback.rating} of 5` };
  }
  if (feedback.kind === 'agent') {
    return feedback.rating > 0
      ? { icon: Smile, tone: 'success', label: 'Positive' }
      : feedback.rating < 0
        ? { icon: Frown, tone: 'danger', label: 'Negative' }
        : { icon: Meh, tone: 'neutral', label: 'Neutral' };
  }
  return feedback.rating > 0 ? { icon: ThumbsUp, tone: 'success', label: 'Helpful' } : { icon: ThumbsDown, tone: 'danger', label: 'Not helpful' };
}

function Stars({ rating }: { rating: number }) {
  return (
    <span className="inline-flex" aria-hidden>
      {[1, 2, 3, 4, 5].map((star) => (
        <Star key={star} className={clsx('size-3.5', star <= rating ? 'fill-warning text-warning' : 'text-border-strong')} />
      ))}
    </span>
  );
}

export function FeedbackItem({ feedback, names, onAdd }: { feedback: FeedbackDto; names: Names; onAdd: ((source: DatasetQuestionSource) => void) | null }) {
  const returnState = useReturnState('Feedback');
  const to = feedback.widgetId ? `/inbox/${feedback.conversationId}` : `/chat/${feedback.conversationId}`;
  const addable = onAdd && feedback.kind === 'answer' && feedback.rating < 0 && feedback.messageId;
  const agent = names.agent(feedback.agentId);
  const { icon: Icon, tone, label } = ratingLook(feedback);
  return (
    <li className="flex gap-3 px-4 py-4">
      <span aria-hidden className={clsx('flex size-8 shrink-0 items-center justify-center rounded-full', TONES[tone].subtle, TONES[tone].text)}>
        <Icon className="size-4" />
      </span>
      <div className="min-w-0 flex-1 space-y-2">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className={clsx('inline-flex items-center gap-1.5 text-sm font-medium', TONES[tone].text)}>
            {feedback.kind === 'conversation' ? <Stars rating={feedback.rating} /> : null}
            {label}
          </span>
          <Badge>{KIND_LABELS[feedback.kind]}</Badge>
          <span className="text-xs text-fg-muted">
            {feedback.kind === 'agent' ? 'From the conversation' : feedback.author === 'staff' ? 'By staff' : 'By a visitor'},{' '}
            <time dateTime={feedback.createdAt} title={formatDateTime(feedback.createdAt)}>
              {formatRelativeTime(feedback.createdAt)}
            </time>
          </span>
        </div>
        {feedback.comment ? <p className="text-sm break-words whitespace-pre-line text-fg">{feedback.comment}</p> : null}
        {feedback.reasons.length > 0 ? (
          <div className="flex flex-wrap gap-1">
            {feedback.reasons.map((reason) => (
              <Badge key={reason}>{REASON_LABELS[reason] ?? reason}</Badge>
            ))}
          </div>
        ) : null}
        {feedback.messageText ? (
          <blockquote className="border-l-2 border-border pl-2.5 text-small text-fg-secondary">
            <span className="sr-only">Rated answer: </span>
            {feedback.messageText}
          </blockquote>
        ) : null}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-small">
          <Link to={to} state={returnState} className="font-medium text-accent-text hover:underline">
            {feedback.conversationTitle || 'Open the conversation'}
          </Link>
          <span className="text-fg-muted">
            {names.widget(feedback.widgetId)}
            {agent ? ` · ${agent}` : ''}
          </span>
          {addable ? (
            <Button
              size="sm"
              variant="ghost"
              icon={ClipboardPlus}
              className="sm:ml-auto"
              onClick={() => onAdd({ question: '', answer: feedback.messageText, conversationId: feedback.conversationId, messageId: feedback.messageId })}
            >
              Add to dataset
            </Button>
          ) : null}
        </div>
      </div>
    </li>
  );
}
