import { clsx } from 'clsx';
import { ArrowUp } from 'lucide-react';
import { useId, useLayoutEffect, type KeyboardEvent, type RefObject } from 'react';
import { useWidget } from './context';

const MAX_HEIGHT = 128;

export interface ComposerProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: (message: string) => void;
  // Typing goes on while an answer is written; sending waits.
  sendBlocked: boolean;
  glowing?: boolean;
  textareaRef: RefObject<HTMLTextAreaElement | null>;
}

export function Composer({ value, onChange, onSubmit, sendBlocked, glowing = false, textareaRef }: ComposerProps) {
  const { config, t, mark } = useWidget();
  const inputId = useId();
  const countId = useId();
  const maxLength = config.maxMessageLength;
  const remaining = maxLength - value.length;
  const showCount = remaining <= Math.min(100, Math.ceil(maxLength * 0.1));
  const canSend = value.trim() !== '' && !sendBlocked && remaining >= 0;

  // Grows with its text, also when the text is set from outside; measured before paint.
  useLayoutEffect(() => {
    const textarea = textareaRef.current;
    if (textarea) {
      textarea.style.height = 'auto';
      textarea.style.height = `${Math.min(textarea.scrollHeight, MAX_HEIGHT)}px`;
    }
  }, [value, textareaRef]);

  const submit = () => {
    if (canSend) {
      onSubmit(value.trim());
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.altKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit();
    }
  };

  return (
    <form
      className="px-3 pt-2"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <div
        {...mark('behavior.placeholder')}
        className={clsx(
          'flex items-end gap-2 rounded-[22px] border border-line bg-raised py-1.5 pr-1.5 pl-4',
          'focus-within:outline-2 focus-within:outline-offset-1 focus-within:outline-accent-text forced-colors:border-[CanvasText]',
          glowing && 'ka-glow-ring',
        )}
      >
        <label htmlFor={inputId} className="sr-only">
          {t('message')}
        </label>
        <textarea
          id={inputId}
          ref={textareaRef}
          rows={1}
          value={value}
          maxLength={maxLength}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={onKeyDown}
          placeholder={config.placeholder ?? t('placeholder')}
          aria-describedby={showCount ? countId : undefined}
          className="block max-h-32 min-h-8 flex-1 resize-none bg-transparent py-1 text-[15px] leading-6 text-ink placeholder:text-muted focus:outline-none"
        />
        <button
          type="submit"
          disabled={!canSend}
          aria-label={t('send')}
          className="grid size-8 shrink-0 place-items-center rounded-full bg-accent text-on-accent shadow-sm transition-[transform,background-color,opacity] duration-200 ease-(--ease-spring) hover:bg-accent-hover active:scale-90 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <ArrowUp className="size-4.5" aria-hidden="true" />
        </button>
      </div>
      {showCount ? (
        <p id={countId} className="mt-1 px-1 text-xs text-muted">
          {t('charactersLeft', { count: Math.max(0, remaining) })}
        </p>
      ) : null}
    </form>
  );
}
