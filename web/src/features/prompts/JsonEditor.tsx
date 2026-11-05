import type { KeyboardEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Field } from '../../components/ui/Field';

export interface JsonEditorProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  rows?: number;
  onKeyDown?: (event: KeyboardEvent<HTMLTextAreaElement>) => void;
}

export function JsonEditor({ id, label, value, onChange, error, rows = 12, onKeyDown }: JsonEditorProps) {
  const errors = error ? [error] : undefined;
  return (
    <Field label={label} htmlFor={id} errors={errors} hint="A JSON object; each key is a template variable.">
      <textarea
        {...fieldAria(id, errors, true)}
        value={value}
        rows={rows}
        spellCheck={false}
        autoCapitalize="off"
        autoComplete="off"
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={onKeyDown}
        className="field resize-y font-mono text-xs leading-relaxed"
      />
    </Field>
  );
}
