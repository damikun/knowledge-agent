import { clsx } from 'clsx';
import { LoaderCircle, type LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { ApiError, errorMessage } from '../../lib/api';
import { TONES, type Tone } from './tone';

export function Spinner({ className, label }: { className?: string; label?: string }) {
  return (
    <span className="inline-flex items-center gap-2 text-fg-muted" role={label ? 'status' : undefined}>
      <LoaderCircle className={clsx('size-4 animate-spin', className)} aria-hidden />
      {label ? <span className="text-sm">{label}</span> : null}
    </span>
  );
}

export type AlertTone = Tone | 'accent';

export interface AlertProps {
  tone?: AlertTone;
  title?: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
  icon?: LucideIcon;
  /** role="alert" announces immediately; use for errors that appear after an action. */
  live?: boolean;
}

export function Alert({ tone = 'info', title, children, action, className, icon, live = false }: AlertProps) {
  const resolved = tone === 'accent' ? 'info' : tone;
  const style = TONES[resolved];
  const Icon = icon ?? style.Icon;
  return (
    <div
      role={live ? 'alert' : undefined}
      data-tone={resolved}
      className={clsx('flex gap-2.5 rounded-lg px-3 py-2.5 text-small text-fg-secondary forced-colors:border', style.subtle, className)}
    >
      <Icon className={clsx('mt-px size-4 shrink-0', style.icon)} aria-hidden />
      <div className="min-w-0 flex-1">
        {title ? <div className="font-semibold text-fg">{title}</div> : null}
        {children ? <div className={clsx(title ? 'mt-0.5' : undefined, 'break-words')}>{children}</div> : null}
      </div>
      {action ? <div className="shrink-0 self-center">{action}</div> : null}
    </div>
  );
}

export function ErrorMessage({ error, title, className }: { error: unknown; title?: string; className?: string }) {
  if (!error) {
    return null;
  }
  const fieldErrors = error instanceof ApiError ? Object.entries(error.fieldErrors) : [];
  const showList = fieldErrors.length > 1 || fieldErrors.some(([, messages]) => messages.length > 1);
  return (
    <Alert tone="danger" title={title} className={className} live>
      {showList ? (
        <ul className="list-disc space-y-0.5 pl-4">
          {fieldErrors.flatMap(([field, messages]) =>
            messages.map((message, index) => <li key={`${field}-${index}`}>{message}</li>),
          )}
        </ul>
      ) : (
        errorMessage(error)
      )}
    </Alert>
  );
}

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon?: LucideIcon;
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={clsx('flex flex-col items-center justify-center px-6 py-10 text-center', className)}>
      <div className="flex max-w-100 flex-col items-center">
        {Icon ? (
          <div className="mb-3 flex size-10 items-center justify-center rounded-xl border border-border bg-surface text-fg-muted">
            <Icon className="size-5" aria-hidden />
          </div>
        ) : null}
        <div className="text-sm font-semibold text-fg">{title}</div>
        {description ? <div className="mt-1 text-sm text-fg-secondary">{description}</div> : null}
        {action ? <div className="mt-4">{action}</div> : null}
      </div>
    </div>
  );
}

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={clsx('rounded-2xl border border-border bg-surface shadow-card', className)}>{children}</div>;
}

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <h1 className="min-w-0 text-title text-fg">{title}</h1>
        {actions ? <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
      {description ? <p className="mt-1 max-w-160 text-sm text-fg-secondary">{description}</p> : null}
    </div>
  );
}
