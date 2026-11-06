import { clsx } from 'clsx';
import type { ReactNode } from 'react';

export interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: ReactNode;
  disabled?: boolean;
  describedBy?: string;
}

export function Switch({ checked, onChange, label, disabled = false, describedBy }: SwitchProps) {
  return (
    <label className={clsx('flex min-h-6 w-fit items-center gap-2 text-sm text-fg', disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer')}>
      <input
        type="checkbox"
        role="switch"
        aria-checked={checked}
        aria-describedby={describedBy}
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        className="peer sr-only"
      />
      <span
        aria-hidden
        className="relative inline-flex h-4.5 w-8 shrink-0 rounded-full bg-border-strong transition-colors duration-100 ease-out peer-checked:bg-control peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus after:absolute after:top-0.5 after:left-0.5 after:size-3.5 after:rounded-full after:bg-fg-on-control after:shadow-control after:transition-transform after:duration-100 after:ease-out peer-checked:after:translate-x-3.5 forced-colors:border forced-colors:border-[ButtonText]"
      />
      {label}
    </label>
  );
}
