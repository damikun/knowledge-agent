import { ArrowLeft, ArrowRight, Plus } from 'lucide-react';
import { useRef, type FormEvent, type ReactNode, type Ref } from 'react';
import { useOnChange } from '../../lib/hooks/useOnChange';
import { Button } from './Button';
import { Page } from './Page';

export interface WizardFrameProps {
  /** The current step's id: a new step is announced by moving focus to its title. */
  step: string;
  progress: ReactNode;
  onCancel: () => void;
  formId: string;
  formRef?: Ref<HTMLFormElement>;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  title: ReactNode;
  description: ReactNode;
  /** A step opened for a refused field focuses that field instead of the title. */
  invalidControl?: () => HTMLElement | null | undefined;
  onBack?: () => void;
  last: boolean;
  createLabel: string;
  creating: boolean;
  canSubmit?: boolean;
  children: ReactNode;
}

export function WizardFrame({
  step,
  progress,
  onCancel,
  formId,
  formRef,
  onSubmit,
  title,
  description,
  invalidControl,
  onBack,
  last,
  createLabel,
  creating,
  canSubmit = true,
  children,
}: WizardFrameProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  // The step a page opens on keeps the browser's focus.
  useOnChange(step, () => (invalidControl?.() ?? headingRef.current)?.focus());

  return (
    <Page variant="workspace">
      <div className="shrink-0 border-b border-border-subtle">
        <div className="mx-auto flex max-w-3xl items-center gap-4 px-4 py-3 lg:px-6">
          {progress}
          <Button variant="ghost" className="ml-auto" onClick={onCancel} title="Esc">
            Cancel
          </Button>
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        <form
          ref={formRef}
          id={formId}
          onSubmit={onSubmit}
          noValidate
          aria-labelledby={`${formId}-title`}
          className="@container mx-auto max-w-3xl space-y-6 px-4 py-8 lg:px-6"
        >
          <div>
            <h1 ref={headingRef} id={`${formId}-title`} tabIndex={-1} className="text-title text-fg outline-none">
              {title}
            </h1>
            <p className="mt-1 max-w-160 text-sm text-fg-secondary">{description}</p>
          </div>
          {children}
        </form>
      </div>

      <div className="shrink-0 border-t border-border-subtle bg-canvas">
        <div className="mx-auto flex max-w-3xl items-center gap-2 px-4 py-3 lg:px-6">
          {onBack ? (
            <Button variant="ghost" icon={ArrowLeft} onClick={onBack}>
              Back
            </Button>
          ) : null}
          {canSubmit ? (
            <Button type="submit" form={formId} variant="primary" className="ml-auto" icon={last ? Plus : undefined} loading={creating}>
              {last ? createLabel : 'Continue'}
              {last ? null : <ArrowRight className="size-4" aria-hidden />}
            </Button>
          ) : null}
        </div>
      </div>
    </Page>
  );
}
