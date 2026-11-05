import { clsx } from 'clsx';
import type { LucideIcon } from 'lucide-react';
import { useId, type ReactNode } from 'react';

export interface SegmentedOption<T extends string> {
  value: T;
  label: ReactNode;
  icon?: LucideIcon;
}

export interface SegmentedControlProps<T extends string> {
  label: string;
  options: readonly SegmentedOption<T>[];
  value: T;
  onChange: (value: T) => void;
  disabled?: boolean;
  /** Shows only the icons, with the labels for screen readers and as tooltips. */
  iconOnly?: boolean;
  className?: string;
}

/** Native radios, so arrow keys work. Use Tabs when each option shows its own panel. */
export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
  disabled = false,
  iconOnly = false,
  className,
}: SegmentedControlProps<T>) {
  const name = useId();
  return (
    <div role="radiogroup" aria-label={label} className={clsx('inline-flex h-8 items-center gap-0.5 rounded-lg bg-fill-muted p-0.5', className)}>
      {options.map(({ value: option, label: optionLabel, icon: Icon }) => (
        <label
          key={option}
          title={iconOnly && typeof optionLabel === 'string' ? optionLabel : undefined}
          className={clsx('relative', disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer')}
        >
          <input
            type="radio"
            name={name}
            value={option}
            checked={option === value}
            disabled={disabled}
            onChange={() => onChange(option)}
            className="peer sr-only"
          />
          <span
            className={clsx(
              'inline-flex h-7 items-center gap-1.5 rounded-md text-small',
              iconOnly ? 'w-8 justify-center' : 'px-2.5',
              'font-medium whitespace-nowrap text-fg-secondary peer-checked:bg-segment-active peer-checked:text-fg peer-checked:shadow-control peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus hover:text-fg forced-colors:peer-checked:outline-2 forced-colors:peer-checked:outline-[Highlight]',
            )}
          >
            {Icon ? <Icon className="size-4 shrink-0" aria-hidden /> : null}
            {iconOnly ? <span className="sr-only">{optionLabel}</span> : optionLabel}
          </span>
        </label>
      ))}
    </div>
  );
}
