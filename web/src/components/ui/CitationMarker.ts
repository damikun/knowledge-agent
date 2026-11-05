import { clsx } from 'clsx';

export type CitationMarkerTone = 'default' | 'open' | 'unknown';

/** Like buttonClass, for a raised citation number. `unknown` marks a citation that matches no passage. */
export function citationMarkerClass({ tone = 'default', interactive = false }: { tone?: CitationMarkerTone; interactive?: boolean } = {}): string {
  return clsx(
    'not-prose relative -top-[0.45em] mx-px inline-flex h-4 min-w-4 items-center justify-center rounded-full px-1 align-baseline text-[0.6875rem] leading-none font-medium tabular-nums no-underline',
    interactive && 'cursor-pointer',
    tone === 'open' && 'bg-accent-subtle text-accent-text',
    tone === 'default' && 'bg-fill-muted text-fg-secondary hover:bg-accent-subtle hover:text-accent-text',
    tone === 'unknown' && 'text-fg-muted ring-1 ring-border ring-inset',
  );
}
