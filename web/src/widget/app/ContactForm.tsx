import { useId, useState, type ComponentProps, type FormEvent } from 'react';
import { AccentButton } from './AccentButton';
import { useWidget } from './context';
import type { FieldErrors, FormResult } from './controller';

export interface ContactFormProps {
  initialName: string | null;
  initialEmail: string | null;
  initialErrors?: FieldErrors;
  onSubmit: (contact: { name: string; email: string }) => Promise<FormResult>;
  onCancel?: () => void;
  // On touch devices, focus would open the keyboard unasked.
  autoFocus?: boolean;
}

interface ContactFieldProps extends Omit<ComponentProps<'input'>, 'id' | 'value' | 'onChange' | 'className'> {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | undefined;
}

function ContactField({ label, value, onChange, error, ...input }: ContactFieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="block text-xs font-medium text-muted">
        {label}
      </label>
      <input
        id={id}
        {...input}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        className="block w-full rounded-xl border border-control bg-raised px-3 py-2 text-[14.5px] text-ink placeholder:text-muted aria-invalid:border-danger"
      />
      {error ? (
        <p id={errorId} className="text-xs text-danger">
          {error}
        </p>
      ) : null}
    </div>
  );
}

// Also runs when autoFocus turns on after mount.
function focusField(input: HTMLInputElement | null): void {
  input?.focus({ preventScroll: true });
}

export function ContactForm({ initialName, initialEmail, initialErrors, onSubmit, onCancel, autoFocus = false }: ContactFormProps) {
  const { t, mark } = useWidget();
  const [name, setName] = useState(initialName ?? '');
  const [email, setEmail] = useState(initialEmail ?? '');
  const [errors, setErrors] = useState<FieldErrors>(initialErrors ?? {});
  const [busy, setBusy] = useState(false);
  const titleId = useId();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    const result = await onSubmit({ name, email });
    setBusy(false);
    if (!result.ok) {
      setErrors(result.errors);
    }
  };

  return (
    <form {...mark('behavior.contact')} onSubmit={(event) => void submit(event)} aria-labelledby={titleId} className="space-y-3 px-4 pt-3 pb-2">
      <h2 id={titleId} className="text-[15px] font-semibold tracking-[-0.01em] text-ink">
        {t('contactTitle')}
      </h2>
      <ContactField
        ref={autoFocus ? focusField : undefined}
        label={t('contactName')}
        autoComplete="name"
        maxLength={200}
        value={name}
        onChange={setName}
        error={errors.name}
      />
      <ContactField
        label={t('contactEmail')}
        type="email"
        required
        autoComplete="email"
        maxLength={320}
        value={email}
        onChange={setEmail}
        error={errors.email}
      />
      <div className="flex gap-2 pt-0.5">
        <AccentButton type="submit" disabled={busy}>
          {t('contactSubmit')}
        </AccentButton>
        {onCancel ? (
          <button type="button" onClick={onCancel} className="rounded-full px-4 py-2 text-sm font-medium text-ink transition-colors hover:bg-raised">
            {t('cancel')}
          </button>
        ) : null}
      </div>
    </form>
  );
}
