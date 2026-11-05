import { clsx } from 'clsx';
import type { ReactNode } from 'react';

/** For inputs that take codes, keys and addresses: no autofill, capitals or spelling fixes. */
export const inputProps = { autoComplete: 'off', autoCapitalize: 'off', autoCorrect: 'off', spellCheck: false } as const;

export interface FieldProps {
  label: ReactNode;
  htmlFor: string;
  hint?: ReactNode;
  errors?: string[];
  optional?: boolean;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}

/** The hint and error ids derive from `htmlFor`; see fieldAria. */
export function Field({ label, htmlFor, hint, errors, optional, action, className, children }: FieldProps) {
  const labelElement = (
    <label htmlFor={htmlFor} className="flex items-baseline gap-1.5 text-small font-medium text-fg">
      {label}
      {optional ? <span className="font-normal text-fg-muted">optional</span> : null}
    </label>
  );
  return (
    <div className={clsx('space-y-1.5', className)}>
      {action ? (
        <div className="flex items-baseline justify-between gap-2">
          {labelElement}
          {action}
        </div>
      ) : (
        labelElement
      )}
      {children}
      {errors && errors.length > 0 ? (
        <p id={`${htmlFor}-error`} className="text-xs text-danger-text">
          {errors.join(' ')}
        </p>
      ) : hint ? (
        <p id={`${htmlFor}-hint`} className="text-xs text-fg-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

export function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="rounded-sm border border-border bg-surface-subtle px-1 font-sans text-[10px] font-medium text-fg-secondary">{children}</kbd>
  );
}
