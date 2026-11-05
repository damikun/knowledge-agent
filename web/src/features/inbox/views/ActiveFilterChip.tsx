import { X } from 'lucide-react';
import type { ReactNode } from 'react';

export interface ActiveFilterChipProps {
  /** The filter as one phrase, e.g. "Rating: 1-2", for button labels. */
  text: string;
  children: ReactNode;
  /** Without it the chip is display-only. */
  onEdit?: (anchor: HTMLButtonElement) => void;
  editing?: boolean;
  onRemove: () => void;
  removeTitle?: string;
}

/** Renders inside a list. */
export function ActiveFilterChip({ text, children, onEdit, editing, onRemove, removeTitle }: ActiveFilterChipProps) {
  return (
    <li className="inline-flex h-6 max-w-full items-center rounded-full bg-accent-subtle text-xs font-medium text-accent-text ring-1 ring-accent/30 ring-inset">
      {onEdit ? (
        <button
          type="button"
          aria-label={`Change filter: ${text}`}
          aria-expanded={editing}
          onClick={(event) => onEdit(event.currentTarget)}
          className="min-w-0 truncate rounded-l-full py-0.5 pr-1 pl-2.5 hover:underline"
        >
          {children}
        </button>
      ) : (
        <span className="min-w-0 truncate py-0.5 pr-1 pl-2.5">{children}</span>
      )}
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Remove filter: ${text}`}
        title={removeTitle}
        className="mr-0.5 inline-flex size-5 shrink-0 items-center justify-center rounded-full hover:bg-accent/15"
      >
        <X className="size-3.5" aria-hidden />
      </button>
    </li>
  );
}
