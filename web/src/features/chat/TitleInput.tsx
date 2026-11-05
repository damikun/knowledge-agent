import { clsx } from 'clsx';
import { useRef, useState } from 'react';

export interface TitleInputProps {
  title: string;
  /** The new title, or null when Esc cancelled. Called once. */
  onDone: (title: string | null) => void;
  className?: string;
}

export function TitleInput({ title, onDone, className }: TitleInputProps) {
  const [value, setValue] = useState(title);
  // Enter ends the rename and unmounts the field, and some browsers then also send a blur: only the first end counts. A
  // ref, because the blur comes in the same event, before any new render.
  const done = useRef(false);
  const finish = (next: string | null) => {
    if (!done.current) {
      done.current = true;
      onDone(next);
    }
  };
  return (
    <input
      autoFocus
      value={value}
      maxLength={200}
      aria-label="Conversation title"
      onFocus={(event) => event.currentTarget.select()}
      onChange={(event) => setValue(event.target.value)}
      onBlur={() => finish(value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          finish(value);
        } else if (event.key === 'Escape') {
          event.preventDefault();
          event.stopPropagation();
          finish(null);
        }
      }}
      className={clsx('h-8 rounded-md border border-focus bg-surface px-2 text-sm text-fg ring-1 ring-focus outline-none', className)}
    />
  );
}
