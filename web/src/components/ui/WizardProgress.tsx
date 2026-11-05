import { clsx } from 'clsx';
import { Check } from 'lucide-react';

interface WizardProgressStep<T extends string> {
  id: T;
  label: string;
}

export interface WizardProgressProps<T extends string> {
  /** The numbered steps; a last check after them is not one. */
  steps: readonly WizardProgressStep<T>[];
  /** The index of the current step; past the last one, the wizard is at its last check. */
  current: number;
  onGo: (step: T) => void;
}

export function WizardProgress<T extends string>({ steps, current, onGo }: WizardProgressProps<T>) {
  return (
    <nav aria-label="Progress" className="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1">
      <p className="text-small font-medium text-fg tabular-nums">
        {current >= steps.length ? 'Last check' : `Step ${current + 1} of ${steps.length}`}
      </p>
      <ol className="flex items-center gap-1">
        {steps.map((item, index) => {
          const done = index < current;
          const here = index === current;
          const marker = (
            <span
              aria-hidden
              className={clsx(
                'flex size-5 shrink-0 items-center justify-center rounded-full text-xs font-semibold tabular-nums',
                here ? 'bg-accent text-fg-on-accent' : done ? 'bg-accent-subtle text-accent-text' : 'bg-fill-muted text-fg-muted',
              )}
            >
              {done ? <Check className="size-3" /> : index + 1}
            </span>
          );
          const label = <span className={clsx('hidden sm:inline', here ? 'font-medium text-fg' : 'text-fg-secondary')}>{item.label}</span>;
          return (
            <li key={item.id} className="flex items-center gap-1">
              {index > 0 ? <span aria-hidden className={clsx('h-px w-3 sm:w-5', done || here ? 'bg-accent' : 'bg-border')} /> : null}
              {done ? (
                <button
                  type="button"
                  onClick={() => onGo(item.id)}
                  aria-label={`${item.label}, done`}
                  className="flex items-center gap-1.5 rounded-md px-1 py-0.5 text-small hover:bg-fill-hover"
                >
                  {marker}
                  {label}
                </button>
              ) : (
                <span aria-current={here ? 'step' : undefined} className="flex items-center gap-1.5 px-1 py-0.5 text-small">
                  {marker}
                  {label}
                  <span className="sr-only sm:hidden">{item.label}</span>
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
