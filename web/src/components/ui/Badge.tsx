import { clsx } from 'clsx';
import type { ComponentProps } from 'react';
import { TONES, type Tone } from './tone';

/** `accent` is the info tone; `outline` renders as neutral. */
export type BadgeTone = Tone | 'accent' | 'outline';

function badgeTone(tone: BadgeTone): Tone {
  return tone === 'accent' ? 'info' : tone === 'outline' ? 'neutral' : tone;
}

export interface BadgeProps extends ComponentProps<'span'> {
  tone?: BadgeTone;
  dot?: boolean;
}

export function Badge({ tone = 'neutral', dot = false, className, children, ...props }: BadgeProps) {
  const resolved = badgeTone(tone);
  const style = TONES[resolved];
  return (
    <span
      data-tone={resolved}
      className={clsx(
        'inline-flex h-5 max-w-full items-center gap-1 rounded-full px-2 text-xs font-medium whitespace-nowrap forced-colors:border',
        style.subtle,
        style.text,
        className,
      )}
      {...props}
    >
      {dot ? <span aria-hidden className={clsx('size-1.5 shrink-0 rounded-full', style.solid)} /> : null}
      {children}
    </span>
  );
}
