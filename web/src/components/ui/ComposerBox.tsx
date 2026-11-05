import type { ReactNode } from 'react';

/** The box, not the textarea, draws the edge and focus ring; hover never overrides focus. */
export function ComposerBox({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-2xl border border-border-strong bg-surface shadow-control transition-[border-color,box-shadow] duration-100 ease-out hover:not-focus-within:border-fg-muted focus-within:border-focus focus-within:ring-1 focus-within:ring-focus">
      {children}
    </div>
  );
}
