import { clsx } from 'clsx';
import { Brain, ChevronRight } from 'lucide-react';
import { useId, useState } from 'react';
import { Markdown } from './Markdown';

export function TypingDots({ className }: { className?: string }) {
  return (
    <span className={clsx('inline-flex items-center gap-0.5', className)} aria-hidden>
      <span className="typing-dot size-1 rounded-full bg-current" />
      <span className="typing-dot size-1 rounded-full bg-current [animation-delay:0.15s]" />
      <span className="typing-dot size-1 rounded-full bg-current [animation-delay:0.3s]" />
    </span>
  );
}

export function ThinkingBlock({ text, active }: { text: string; active: boolean }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return (
    <div>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen((value) => !value)}
        className="-ml-1.5 inline-flex h-7 max-w-full items-center gap-1.5 rounded-md px-1.5 text-xs font-medium text-fg-muted hover:bg-fill-hover hover:text-fg"
      >
        <Brain className="size-3.5 shrink-0" aria-hidden />
        <span>{active ? 'Thinking' : 'Thought process'}</span>
        {active ? <TypingDots /> : null}
        <ChevronRight className={clsx('size-3.5 shrink-0 transition-transform duration-100 ease-out', open && 'rotate-90')} aria-hidden />
      </button>
      {open ? (
        <div id={id} className="mt-1 ml-1 border-l-2 border-border pl-3">
          <Markdown text={text} variant="muted" />
        </div>
      ) : null}
    </div>
  );
}
