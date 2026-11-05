import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { TONES } from './tone';

export interface BannerProps {
  tone: 'warning' | 'danger';
  icon?: LucideIcon;
  children: ReactNode;
  /** One link or button; it takes the banner's text color. */
  action?: ReactNode;
}

/** One blocking problem at the top of the app panel. Show one at a time. */
export function Banner({ tone, icon, children, action }: BannerProps) {
  const style = TONES[tone];
  const Icon = icon ?? style.Icon;
  return (
    <div
      role="status"
      data-tone={tone}
      className={clsx('flex min-h-9 shrink-0 items-center gap-2 px-4 py-1 text-small lg:px-6 forced-colors:border-b', style.subtle, style.text)}
    >
      <Icon className={clsx('size-4 shrink-0', style.icon)} aria-hidden />
      <div className="min-w-0 flex-1">{children}</div>
      {action ? (
        <div className="shrink-0 *:inline-flex *:h-7 *:items-center *:gap-1.5 *:rounded-md *:px-2 *:font-medium *:hover:bg-fill-hover">{action}</div>
      ) : null}
    </div>
  );
}
