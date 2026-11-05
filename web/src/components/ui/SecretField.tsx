import { Eye, EyeOff, Trash2, Undo2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { fieldAria } from './aria';
import { Button } from './Button';
import { Field, inputProps } from './Field';

export interface SecretValue {
  value: string;
  remove: boolean;
}

export const NO_SECRET: SecretValue = { value: '', remove: false };

/** null keeps the saved secret, "" removes it, a value replaces it. */
export function secretRequest(secret: SecretValue): string | null {
  if (secret.remove) {
    return '';
  }
  return secret.value.trim() ? secret.value.trim() : null;
}

export interface SecretFieldProps {
  id: string;
  label: string;
  saved: boolean;
  value: SecretValue;
  onChange: (value: SecretValue) => void;
  errors?: string[];
  placeholder: string;
  hint?: ReactNode;
  disabled?: boolean;
}

export function SecretField({ id, label, saved, value, onChange, errors, placeholder, hint, disabled = false }: SecretFieldProps) {
  const [visible, setVisible] = useState(false);
  const help = value.remove
    ? `The saved ${label.toLowerCase()} is removed when you save.`
    : saved
      ? `Leave empty to keep the saved ${label.toLowerCase()}.`
      : (hint ?? 'Stored encrypted and never shown again.');
  return (
    <div className="space-y-1.5">
      <Field label={label} htmlFor={id} errors={errors} hint={help}>
        <div className="relative">
          <input
            {...fieldAria(id, errors, true)}
            {...inputProps}
            type={visible ? 'text' : 'password'}
            value={value.value}
            onChange={(event) => onChange({ value: event.target.value, remove: false })}
            placeholder={value.remove ? 'Will be removed' : saved ? 'Saved' : placeholder}
            disabled={disabled || value.remove}
            data-1p-ignore="true"
            data-lpignore="true"
            className="field pr-9 font-mono text-xs"
          />
          <Button
            variant="ghost"
            size="icon-sm"
            icon={visible ? EyeOff : Eye}
            aria-label={visible ? `Hide ${label.toLowerCase()}` : `Show ${label.toLowerCase()}`}
            aria-controls={id}
            disabled={disabled || value.remove}
            onClick={() => setVisible((current) => !current)}
            className="absolute top-1/2 right-1 -translate-y-1/2"
          />
        </div>
      </Field>
      {saved ? (
        <Button
          size="sm"
          variant="ghost"
          icon={value.remove ? Undo2 : Trash2}
          disabled={disabled}
          onClick={() => {
            setVisible(false);
            onChange({ value: '', remove: !value.remove });
          }}
        >
          {value.remove ? `Keep saved ${label.toLowerCase()}` : `Remove saved ${label.toLowerCase()}`}
        </Button>
      ) : null}
    </div>
  );
}
