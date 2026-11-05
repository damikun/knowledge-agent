import { Star, ThumbsDown, ThumbsUp } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useWidget } from './context';

const THUMBS = [
  { value: 1, label: 'helpful', Icon: ThumbsUp },
  { value: -1, label: 'notHelpful', Icon: ThumbsDown },
] as const;

export interface AnswerFeedbackProps {
  rating: 1 | -1 | null;
  onRate: (rating: 1 | -1, comment?: string) => Promise<boolean>;
}

export function AnswerFeedback({ rating, onRate }: AnswerFeedbackProps) {
  const { t, mark } = useWidget();
  const [step, setStep] = useState<'idle' | 'comment' | 'thanks'>('idle');
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const commentId = useId();

  const rate = async (value: 1 | -1) => {
    setBusy(true);
    const saved = await onRate(value);
    setBusy(false);
    if (saved) {
      setStep(value === -1 ? 'comment' : 'thanks');
    }
  };

  const submitComment = async (event: FormEvent) => {
    event.preventDefault();
    if (!comment.trim()) {
      setStep('thanks');
      return;
    }
    setBusy(true);
    const saved = await onRate(-1, comment);
    setBusy(false);
    if (saved) {
      setStep('thanks');
    }
  };

  return (
    <div {...mark('behavior.feedback')} className="mt-1 pl-1">
      <div role="group" aria-label={t('feedbackPrompt')} className="flex items-center gap-0.5">
        {THUMBS.map(({ value, label, Icon }) => (
          <button
            key={value}
            type="button"
            className="grid size-7 place-items-center rounded-full text-muted transition-colors hover:bg-raised hover:text-ink aria-pressed:text-accent-text disabled:opacity-60"
            aria-label={t(label)}
            aria-pressed={rating === value}
            disabled={busy}
            onClick={() => void rate(value)}
          >
            <Icon className="size-3.5" fill={rating === value ? 'currentColor' : 'none'} aria-hidden="true" />
          </button>
        ))}
        {step === 'thanks' ? (
          <span role="status" className="ml-1 text-xs text-muted">
            {t('feedbackThanks')}
          </span>
        ) : null}
      </div>
      {step === 'comment' ? (
        <form onSubmit={(event) => void submitComment(event)} className="mt-1.5 space-y-1.5">
          <label htmlFor={commentId} className="block text-xs text-muted">
            {t('feedbackComment')}
          </label>
          <textarea
            id={commentId}
            rows={2}
            maxLength={500}
            value={comment}
            onChange={(event) => setComment(event.target.value)}
            className="block w-full resize-none rounded-xl border border-control bg-raised px-3 py-2 text-sm text-ink"
          />
          <div className="flex gap-2">
            <button type="submit" disabled={busy} className="rounded-full bg-accent px-3.5 py-1.5 text-xs font-semibold text-on-accent transition-colors hover:bg-accent-hover disabled:opacity-60">
              {t('send')}
            </button>
            <button type="button" onClick={() => setStep('thanks')} className="rounded-full px-3.5 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-raised">
              {t('skip')}
            </button>
          </div>
        </form>
      ) : null}
    </div>
  );
}

export interface ConversationRatingProps {
  rating: number | null;
  onRate: (rating: number) => Promise<boolean>;
}

export function ConversationRating({ rating, onRate }: ConversationRatingProps) {
  const { t } = useWidget();
  const [busy, setBusy] = useState(false);
  const [thanked, setThanked] = useState(false);
  const promptId = useId();

  const rate = async (value: number) => {
    setBusy(true);
    const saved = await onRate(value);
    setBusy(false);
    if (saved) {
      setThanked(true);
    }
  };

  return (
    <div className="space-y-1">
      <p id={promptId} className="text-sm text-ink">
        {t('ratingPrompt')}
      </p>
      <div role="group" aria-labelledby={promptId} className="flex justify-center gap-1">
        {[1, 2, 3, 4, 5].map((value) => (
          <button
            key={value}
            type="button"
            aria-label={t('rating', { count: value })}
            aria-pressed={rating === value}
            disabled={busy}
            onClick={() => void rate(value)}
            className="grid size-9 place-items-center rounded-full text-accent-text transition-[transform,background-color] duration-200 ease-(--ease-spring) hover:scale-110 hover:bg-raised disabled:opacity-60"
          >
            <Star className="size-5" fill={rating !== null && value <= rating ? 'currentColor' : 'none'} aria-hidden="true" />
          </button>
        ))}
      </div>
      {thanked || rating !== null ? (
        <p role="status" className="text-xs text-muted">
          {t('feedbackThanks')}
        </p>
      ) : null}
    </div>
  );
}
