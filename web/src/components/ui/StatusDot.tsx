import { clsx } from 'clsx';
import type { ReactNode } from 'react';
import { TONES, type Tone } from './tone';

export interface StatusDotProps {
  tone: Tone;
  pulse?: boolean;
  /** Names the state for screen readers. Without it the dot is decorative, and text next to it must say the same. */
  label?: string;
  className?: string;
}

export function StatusDot({ tone, pulse = false, label, className }: StatusDotProps) {
  return (
    <span
      data-tone={tone}
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      className={clsx('inline-block size-2 shrink-0 rounded-full forced-colors:bg-[CanvasText]', TONES[tone].solid, pulse && 'animate-pulse', className)}
    />
  );
}

export function StatusLabel({ tone, children, className }: { tone: Tone; children: ReactNode; className?: string }) {
  return (
    <span data-tone={tone} className={clsx('inline-flex min-w-0 items-center gap-1.5 text-xs font-medium whitespace-nowrap text-fg-secondary', className)}>
      <StatusDot tone={tone} />
      {children}
    </span>
  );
}
