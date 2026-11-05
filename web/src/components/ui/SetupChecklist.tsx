import { clsx } from 'clsx';
import { Check, ChevronDown } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Button, buttonClass, type ButtonVariant } from './Button';
import { useReturnState } from '../../lib/returnTo';

type SetupStepState = 'done' | 'current' | 'todo';

export interface SetupStep {
  id: string;
  title: ReactNode;
  description?: ReactNode;
  state: SetupStepState;
  action?: { label: string; to?: string; onClick?: () => void };
}

export interface SetupChecklistProps {
  title: ReactNode;
  steps: readonly SetupStep[];
  className?: string;
}

const stateLabels: Record<SetupStepState, string> = { done: 'done', current: 'current step', todo: 'to do' };

function StepMark({ state }: { state: SetupStepState }) {
  if (state === 'done') {
    return (
      <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-success-subtle text-success-text">
        <Check className="size-3" strokeWidth={3} aria-hidden />
      </span>
    );
  }
  return (
    <span className={clsx('flex size-5 shrink-0 items-center justify-center rounded-full border-2', state === 'current' ? 'border-accent' : 'border-border-strong')}>
      {state === 'current' ? <span className="size-2 rounded-full bg-accent" /> : null}
    </span>
  );
}

function StepAction({ action, variant }: { action: NonNullable<SetupStep['action']>; variant: ButtonVariant }) {
  const returnState = useReturnState();
  return action.to ? (
    <Link to={action.to} state={returnState} className={buttonClass({ variant, size: 'sm' })}>
      {action.label}
    </Link>
  ) : (
    <Button variant={variant} size="sm" onClick={action.onClick}>
      {action.label}
    </Button>
  );
}

function StepRow({ step, hidden = false }: { step: SetupStep; hidden?: boolean }) {
  const current = step.state === 'current';
  return (
    <li
      hidden={hidden}
      aria-current={current ? 'step' : undefined}
      className={clsx(
        'relative flex flex-wrap items-start gap-x-3 gap-y-2 px-5',
        current ? 'bg-accent-subtle py-4 before:absolute before:inset-y-0 before:left-0 before:w-[3px] before:bg-accent' : 'py-3',
      )}
    >
      <StepMark state={step.state} />
      <div className="min-w-0 flex-1 basis-48">
        <p className={clsx('text-sm', step.state === 'done' ? 'text-fg-muted' : current ? 'font-semibold text-fg' : 'font-medium text-fg-secondary')}>
          {step.title}
          <span className="sr-only">, {stateLabels[step.state]}</span>
        </p>
        {step.description && step.state !== 'done' ? (
          <p className={clsx('mt-0.5 text-small', current ? 'text-fg-secondary' : 'text-fg-muted')}>{step.description}</p>
        ) : null}
      </div>
      {step.action && step.state !== 'done' ? <StepAction action={step.action} variant={current ? 'primary' : 'ghost'} /> : null}
    </li>
  );
}

export function SetupChecklist({ title, steps, className }: SetupChecklistProps) {
  const titleId = useId();
  const doneListId = useId();
  const [showDone, setShowDone] = useState(false);
  const done = steps.filter((step) => step.state === 'done');
  const open = steps.filter((step) => step.state !== 'done');
  return (
    <section aria-labelledby={titleId} className={clsx('overflow-hidden rounded-2xl border border-border bg-surface shadow-card', className)}>
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-5 pt-4">
        <h2 id={titleId} className="text-section text-fg">
          {title}
        </h2>
        <p className="text-small text-fg-muted tabular-nums">
          {done.length} of {steps.length} done
        </p>
      </div>
      <div aria-hidden className="mx-5 mt-3 h-1.5 overflow-hidden rounded-full bg-fill-muted">
        <div className="h-full rounded-full bg-accent" style={{ width: `${steps.length > 0 ? (done.length / steps.length) * 100 : 0}%` }} />
      </div>
      <ol id={doneListId} className="mt-4 divide-y divide-border-subtle border-t border-border-subtle">
        {open.map((step) => (
          <StepRow key={step.id} step={step} />
        ))}
        {done.map((step) => (
          <StepRow key={step.id} step={step} hidden={!showDone} />
        ))}
      </ol>
      {done.length > 0 ? (
        <button
          type="button"
          aria-expanded={showDone}
          aria-controls={doneListId}
          onClick={() => setShowDone((value) => !value)}
          className="flex w-full items-center gap-2 border-t border-border-subtle px-5 py-2.5 text-small text-fg-muted hover:bg-fill-hover hover:text-fg"
        >
          <span className="flex size-5 items-center justify-center rounded-full bg-success-subtle text-success-text">
            <Check className="size-3" strokeWidth={3} aria-hidden />
          </span>
          {showDone ? 'Hide finished steps' : `${done.length} finished ${done.length === 1 ? 'step' : 'steps'}`}
          <ChevronDown className={clsx('ml-auto size-4', showDone && 'rotate-180')} aria-hidden />
        </button>
      ) : null}
    </section>
  );
}
