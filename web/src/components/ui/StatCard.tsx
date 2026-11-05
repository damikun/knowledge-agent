import { clsx } from 'clsx';
import { Minus, TrendingDown, TrendingUp } from 'lucide-react';
import type { ReactNode } from 'react';
import { TONES } from './tone';

export interface StatTrend {
  direction: 'up' | 'down' | 'flat';
  /** Say the direction in words, e.g. "+12% on last week"; the arrow only repeats it. */
  label: ReactNode;
  /** Whether the change is good news: fewer handovers going down is success. */
  tone?: 'success' | 'danger' | 'neutral';
}

export interface StatCardProps {
  label: ReactNode;
  value: ReactNode;
  caption?: ReactNode;
  trend?: StatTrend;
  className?: string;
}

const trendIcons = { up: TrendingUp, down: TrendingDown, flat: Minus };

export function StatCard({ label, value, caption, trend, className }: StatCardProps) {
  const TrendIcon = trend ? trendIcons[trend.direction] : null;
  return (
    <div className={clsx('min-w-0 rounded-2xl border border-border bg-surface p-4 shadow-card', className)}>
      <p className="truncate text-small text-fg-secondary">{label}</p>
      <p className="mt-1 text-kpi text-fg tabular-nums">{value}</p>
      {trend || caption ? (
        <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-fg-muted">
          {trend && TrendIcon ? (
            <span data-tone={trend.tone ?? 'neutral'} className={clsx('inline-flex items-center gap-1 font-medium tabular-nums', TONES[trend.tone ?? 'neutral'].text)}>
              <TrendIcon className="size-3.5 shrink-0" aria-hidden />
              {trend.label}
            </span>
          ) : null}
          {caption ? <span>{caption}</span> : null}
        </p>
      ) : null}
    </div>
  );
}
