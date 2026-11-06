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

// Warnings and errors differ in shape as well as color, so they read apart without color (WCAG 1.4.1).
const shapes: Record<Tone, string> = {
  neutral: 'rounded-full',
  info: 'rounded-full',
  success: 'rounded-full',
  warning: '[clip-path:polygon(50%_0,100%_100%,0_100%)] scale-125',
  danger: 'rounded-[1px]',
};

export function StatusDot({ tone, pulse = false, label, className }: StatusDotProps) {
  return (
    <span
      data-tone={tone}
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      data-shape={tone === 'warning' ? 'triangle' : tone === 'danger' ? 'square' : 'circle'}
      className={clsx(
        'inline-block size-2 shrink-0 forced-colors:bg-[CanvasText]',
        shapes[tone],
        TONES[tone].solid,
        pulse && 'animate-pulse',
        className,
      )}
    />
  );
}

export function StatusLabel({ tone, children, className }: { tone: Tone; children: ReactNode; className?: string }) {
  return (
    <span
      data-tone={tone}
      className={clsx('inline-flex min-w-0 items-center gap-1.5 text-xs font-medium whitespace-nowrap text-fg-secondary', className)}
    >
      <StatusDot tone={tone} />
      {children}
    </span>
  );
}
