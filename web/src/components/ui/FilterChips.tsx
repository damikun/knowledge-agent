import { clsx } from 'clsx';
import { Check } from 'lucide-react';
import { useId } from 'react';
import { formatNumber } from '../../lib/format';
import { StatusDot } from './StatusDot';
import type { Tone } from './tone';

export interface FilterChipOption<T extends string> {
  value: T;
  label: string;
  count?: number;
  dot?: Tone;
}

export interface FilterChipsProps<T extends string> {
  label: string;
  options: readonly FilterChipOption<T>[];
  value: T;
  onChange: (value: T) => void;
  className?: string;
}

/** Native radios, so arrow keys and screen readers work. The chosen pill stays neutral to leave the accent to the primary action. */
export function FilterChips<T extends string>({ label, options, value, onChange, className }: FilterChipsProps<T>) {
  const name = useId();
  return (
    <div role="radiogroup" aria-label={label} className={clsx('flex flex-wrap items-center gap-1.5', className)}>
      {options.map((option) => (
        <label key={option.value} className="relative cursor-pointer">
          <input
            type="radio"
            name={name}
            value={option.value}
            checked={option.value === value}
            onChange={() => onChange(option.value)}
            className="peer sr-only"
          />
          <span className="inline-flex h-8 items-center gap-1.5 rounded-full border border-border px-3 text-small font-medium whitespace-nowrap text-fg-secondary peer-checked:border-border-strong peer-checked:bg-fill-active peer-checked:text-fg peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus hover:bg-fill-hover hover:text-fg forced-colors:peer-checked:outline-2 forced-colors:peer-checked:outline-[Highlight]">
            {/* The chosen chip shows a check too, so the choice does not rest on the fill alone (WCAG 1.4.1). */}
            {option.value === value ? <Check className="-ml-0.5 size-3.5 shrink-0" aria-hidden /> : null}
            {option.dot ? <StatusDot tone={option.dot} /> : null}
            {option.label}
            {option.count !== undefined ? (
              <span className="font-normal text-fg-muted tabular-nums">
                <span className="sr-only">, </span>
                {formatNumber(option.count)}
              </span>
            ) : null}
          </span>
        </label>
      ))}
    </div>
  );
}
