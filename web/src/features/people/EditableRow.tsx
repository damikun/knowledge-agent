import { Check, Pencil, X } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Button } from '../../components/ui/Button';
import { Select } from '../../components/ui/Select';
import { toast } from '../../components/ui/Toast';
import { errorMessage } from '../../lib/api';

export type EditableKind = 'text' | 'email' | 'number' | 'date' | 'boolean';

export interface EditableRowProps {
  label: string;
  /** '' for none. */
  value: string;
  /** Defaults to the value or a muted "Not set". */
  display?: ReactNode;
  kind?: EditableKind;
  /** Without it the row is read-only. */
  onSave?: (value: string) => Promise<unknown>;
  badge?: ReactNode;
  /** Shown while editing. */
  warning?: ReactNode;
  maxLength?: number;
  /** On by default. */
  clearable?: boolean;
}

const BOOLEAN_OPTIONS = [
  { value: 'true', label: 'Yes' },
  { value: 'false', label: 'No' },
] as const;

// Stable callback refs, so they run on mount and not on every render.
function focusAndSelect(input: HTMLInputElement | null) {
  input?.focus();
  input?.select();
}

function focusElement(element: HTMLElement | null) {
  element?.focus();
}

export function EditableRow({ label, value, display, kind = 'text', onSave, badge, warning, maxLength, clearable = true }: EditableRowProps) {
  const id = useId();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // After the first edit, the edit button takes focus back whenever the row returns to reading.
  const [edited, setEdited] = useState(false);

  const start = () => {
    setDraft(value);
    setError(null);
    setEditing(true);
  };

  const cancel = () => {
    setEdited(true);
    setEditing(false);
    setError(null);
  };

  const save = async (next: string) => {
    if (!onSave) {
      return;
    }
    const trimmed = next.trim();
    if (trimmed === value) {
      cancel();
      return;
    }
    if (!trimmed && !clearable) {
      setError(`${label} cannot be empty.`);
      return;
    }
    setSaving(true);
    setError(null);
    const previous = value;
    try {
      await onSave(trimmed);
      setEdited(true);
      setEditing(false);
      toast({
        title: `${label} saved`,
        tone: 'success',
        action: {
          label: 'Undo',
          onClick: () => {
            onSave(previous).catch((undoError: unknown) => toast({ title: 'The change could not be undone', description: errorMessage(undoError), tone: 'danger' }));
          },
        },
      });
    } catch (saveError) {
      setError(errorMessage(saveError));
    } finally {
      setSaving(false);
    }
  };

  const shown = display ?? (value ? value : <span className="text-fg-muted">Not set</span>);

  return (
    <div className="group flex min-h-8 items-start gap-3 py-1 text-small">
      <span id={`${id}-label`} className="w-28 shrink-0 pt-1 text-fg-muted">
        {label}
      </span>
      {editing ? (
        <div className="min-w-0 flex-1 space-y-1.5">
          <div className="flex items-center gap-1.5">
            {kind === 'boolean' ? (
              <div className="min-w-0 flex-1">
                <Select
                  aria-label={label}
                  size="sm"
                  value={draft === 'true' || draft === 'false' ? draft : 'true'}
                  options={BOOLEAN_OPTIONS}
                  onChange={(next) => void save(next)}
                />
              </div>
            ) : (
              <input
                ref={focusAndSelect}
                aria-labelledby={`${id}-label`}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? `${id}-error` : warning ? `${id}-warning` : undefined}
                className="field h-7 min-w-0 flex-1 py-0 text-small"
                type={kind === 'email' ? 'email' : kind === 'number' ? 'number' : kind === 'date' ? 'date' : 'text'}
                value={draft}
                maxLength={maxLength}
                disabled={saving}
                onChange={(event) => setDraft(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    void save(draft);
                  } else if (event.key === 'Escape') {
                    event.preventDefault();
                    event.stopPropagation();
                    cancel();
                  }
                }}
              />
            )}
            <Button size="icon-sm" variant="ghost" icon={Check} aria-label={`Save ${label}`} loading={saving} onClick={() => void save(draft)} />
            <Button size="icon-sm" variant="ghost" icon={X} aria-label={`Cancel editing ${label}`} disabled={saving} onClick={cancel} />
          </div>
          {warning ? (
            <p id={`${id}-warning`} className="text-xs text-warning-text">
              {warning}
            </p>
          ) : null}
          {error ? (
            <p id={`${id}-error`} role="alert" className="text-xs text-danger-text">
              {error}
            </p>
          ) : null}
        </div>
      ) : (
        <>
          <div className="flex min-w-0 flex-1 flex-wrap items-center gap-1.5 pt-1">
            <span className="min-w-0 break-words text-fg">{shown}</span>
            {badge}
          </div>
          {onSave ? (
            <Button
              ref={edited ? focusElement : undefined}
              size="icon-sm"
              variant="ghost"
              icon={Pencil}
              aria-label={`Edit ${label}`}
              onClick={start}
              className="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 focus-visible:opacity-100 pointer-coarse:opacity-100"
            />
          ) : null}
        </>
      )}
    </div>
  );
}
