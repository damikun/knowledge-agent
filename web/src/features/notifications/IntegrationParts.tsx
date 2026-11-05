import { clsx } from 'clsx';
import { Check, Send, type LucideIcon } from 'lucide-react';
import { Fragment, useId, useRef, type FormEvent, type ReactNode } from 'react';
import { Link } from 'react-router';
import { StatusLabel } from '../../components/ui/StatusDot';
import { Badge } from '../../components/ui/Badge';
import type { Tone } from '../../components/ui/tone';
import { Button } from '../../components/ui/Button';
import { Modal } from '../../components/ui/Modal';
import type { WidgetDto } from '../widgets/types';
import { useReturnState } from '../../lib/returnTo';

export interface TestDialogProps {
  open: boolean;
  title: string;
  children: ReactNode;
  busy: boolean;
  onSend: () => void;
  onCancel: () => void;
}

export function TestDialog({ open, title, children, busy, onSend, onCancel }: TestDialogProps) {
  const formId = useId();
  const cancelRef = useRef<HTMLButtonElement>(null);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSend();
  };
  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="sm"
      initialFocus={cancelRef}
      dismissible={!busy}
      footer={
        <>
          <Button ref={cancelRef} onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant="primary" icon={Send} loading={busy}>
            Send
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="space-y-4 text-sm text-fg-secondary">
        {children}
      </form>
    </Modal>
  );
}

export function FormGroup({ legend, description, children }: { legend: string; description?: ReactNode; children: ReactNode }) {
  return (
    <fieldset className="space-y-3 border-t border-border-subtle pt-5">
      <legend className="float-left mb-1 w-full text-sm font-semibold text-fg">{legend}</legend>
      {description ? <p className="clear-left text-xs text-fg-muted">{description}</p> : null}
      <div className="clear-left grid gap-4 sm:grid-cols-2">{children}</div>
    </fieldset>
  );
}

interface SetupItem {
  label: string;
  done: boolean;
  detail?: ReactNode;
}

export interface IntegrationStatus {
  tone: Tone;
  label: string;
}

export interface IntegrationCardProps {
  icon: LucideIcon;
  title: string;
  status: IntegrationStatus;
  fromConfiguration: boolean;
  purpose: ReactNode;
  steps: readonly SetupItem[];
  actions: ReactNode;
  children?: ReactNode;
}

export function IntegrationCard({ icon: Icon, title, status, fromConfiguration, purpose, steps, actions, children }: IntegrationCardProps) {
  const headingId = useId();
  const done = steps.filter((step) => step.done).length;
  return (
    <section aria-labelledby={headingId} className="flex flex-col rounded-2xl border border-border bg-surface">
      <div className="flex items-start gap-3 p-5">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-fill-muted text-fg-secondary">
          <Icon className="size-5" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
            <h3 id={headingId} className="text-section text-fg">
              {title}
            </h3>
            <div className="flex flex-wrap gap-1.5">
              {fromConfiguration ? <Badge>From configuration</Badge> : null}
              <StatusLabel tone={status.tone}>
                {status.label}
              </StatusLabel>
            </div>
          </div>
          <p className="mt-1 text-sm text-fg-secondary">{purpose}</p>
        </div>
      </div>

      <div className="flex-1 space-y-4 border-t border-border-subtle px-5 py-4">
        <div className="flex items-baseline justify-between gap-4">
          <h4 className="text-small font-medium text-fg">Setup</h4>
          <p className="text-xs text-fg-muted tabular-nums">
            {done} of {steps.length} done
          </p>
        </div>
        <ol aria-label={`${title} setup`} className="space-y-3">
          {steps.map((step, index) => (
            <li key={step.label} className="flex items-start gap-3">
              <span
                aria-hidden
                className={clsx(
                  'flex size-5 shrink-0 items-center justify-center rounded-full text-xs font-medium tabular-nums',
                  step.done ? 'bg-success-subtle text-success-text' : 'border border-border-strong text-fg-muted',
                )}
              >
                {step.done ? <Check className="size-3" strokeWidth={3} /> : index + 1}
              </span>
              <div className="min-w-0">
                <p className={clsx('text-sm', step.done ? 'text-fg-secondary' : 'font-medium text-fg')}>
                  {step.label}
                  <span className="sr-only">{step.done ? ', done' : ', to do'}</span>
                </p>
                {step.detail ? <p className="mt-0.5 text-small text-fg-muted">{step.detail}</p> : null}
              </div>
            </li>
          ))}
        </ol>
        {children}
      </div>

      <div className="flex flex-wrap items-center gap-2 rounded-b-2xl border-t border-border-subtle bg-surface-subtle px-5 py-3">{actions}</div>
    </section>
  );
}

/** Undefined while the widgets load. */
export function widgetsSendingTo(widgets: readonly WidgetDto[] | undefined, channel: string): WidgetDto[] | undefined {
  return widgets?.filter((widget) => widget.settings.behavior.handoverEnabled && widget.settings.handover.channel === channel);
}

export function UsedBy({ widgets }: { widgets: readonly WidgetDto[] }) {
  const returnState = useReturnState();
  return (
    <>
      Used by{' '}
      {widgets.map((widget, index) => (
        <Fragment key={widget.id}>
          {index > 0 ? (index === widgets.length - 1 ? ' and ' : ', ') : null}
          <Link to={`/widgets/${widget.id}`} state={returnState} className="font-medium text-accent-text hover:underline">
            {widget.name}
          </Link>
        </Fragment>
      ))}
    </>
  );
}
