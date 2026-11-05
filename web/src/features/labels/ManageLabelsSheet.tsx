import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Pencil, Plus, RefreshCw, Sparkles, Tags, Trash2 } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Field } from '../../components/ui/Field';
import { ConfirmDialog } from '../../components/ui/Modal';
import { SideSheet } from '../../components/ui/SideSheet';
import { Switch } from '../../components/ui/Switch';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { pluralize } from '../../lib/format';
import { labelsApi, labelsOrEmpty, useLabels, useRefreshAfterLabelChange } from './api';
import { LabelChip } from './LabelChip';
import { LABEL_STYLES } from './labelStyles';
import { LABEL_COLORS, type Label, type LabelColor, type LabelInput } from './types';

/** The server queues at most 500 conversations. */
const RELABEL_DAYS = 30;

const EMPTY: LabelInput = {
  name: '',
  color: 'blue',
  description: '',
  autoApply: false,
};

function ColorPicker({ value, onChange, id }: { value: LabelColor; onChange: (color: LabelColor) => void; id: string }) {
  return (
    <div role="radiogroup" aria-labelledby={`${id}-label`} className="flex flex-wrap gap-1.5">
      {LABEL_COLORS.map((color) => {
        const style = LABEL_STYLES[color];
        const checked = color === value;
        return (
          <label
            key={color}
            className={clsx(
              'flex h-7 cursor-pointer items-center gap-1.5 rounded-full px-2.5 text-xs font-medium has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-focus',
              style.chip,
              checked ? 'ring-2 ring-accent' : 'ring-0',
            )}
          >
            <input type="radio" name={`${id}-color`} value={color} checked={checked} onChange={() => onChange(color)} className="sr-only" />
            <span aria-hidden className={clsx('size-2 rounded-full', style.dot)} />
            {style.name}
          </label>
        );
      })}
    </div>
  );
}

interface LabelFormProps {
  label: Label | null;
  onDone: () => void;
}

function LabelForm({ label, onDone }: LabelFormProps) {
  const ids = useId();
  const refresh = useRefreshAfterLabelChange();
  const [input, setInput] = useState<LabelInput>(
    label
      ? {
          name: label.name,
          color: label.color,
          description: label.description,
          autoApply: label.autoApply,
        }
      : EMPTY,
  );
  const save = useMutation({
    mutationFn: () => {
      const body = {
        ...input,
        name: input.name.trim(),
        description: input.description.trim(),
      };
      return label ? labelsApi.update(label.id, body) : labelsApi.create(body);
    },
    onSuccess: (saved) => {
      refresh();
      toast({
        title: label ? 'Label saved' : `Created ${saved.name}`,
        tone: 'success',
      });
      onDone();
    },
  });
  const errors = (field: string) => (save.error instanceof ApiError ? save.error.errorsFor(field) : []);
  const other = save.error instanceof ApiError && save.error.isValidation ? save.error.errorsExcept(['name', 'color', 'description']) : [];

  const submit = (event: FormEvent) => {
    event.preventDefault();
    save.mutate();
  };

  return (
    <form onSubmit={submit} className="space-y-4 rounded-xl border border-border bg-surface-subtle p-4" aria-label={label ? `Edit ${label.name}` : 'New label'}>
      <Field label="Name" htmlFor={`${ids}-name`} errors={errors('name')}>
        <input
          {...fieldAria(`${ids}-name`, errors('name'))}
          value={input.name}
          onChange={(event) => setInput({ ...input, name: event.target.value })}
          maxLength={40}
          required
          autoComplete="off"
          className="field"
        />
      </Field>
      <div className="space-y-1.5">
        <span id={`${ids}-color-label`} className="text-small font-medium text-fg">
          Color
        </span>
        <ColorPicker id={`${ids}-color`} value={input.color} onChange={(color) => setInput({ ...input, color })} />
      </div>
      <Field
        label="What it means"
        htmlFor={`${ids}-description`}
        hint="The auto-labeling reads this to decide when the label fits, such as “The visitor asks about a refund or a return.”"
        errors={errors('description')}
        optional
      >
        <textarea
          {...fieldAria(`${ids}-description`, errors('description'), true)}
          value={input.description}
          onChange={(event) => setInput({ ...input, description: event.target.value })}
          maxLength={500}
          rows={3}
          className="field min-h-20 py-2"
        />
      </Field>
      <div className="space-y-1">
        <Switch
          checked={input.autoApply}
          onChange={(autoApply) => setInput({ ...input, autoApply })}
          label="Apply automatically"
          describedBy={`${ids}-auto-hint`}
        />
        <p id={`${ids}-auto-hint`} className="text-xs text-fg-muted">
          The assistant puts up to two of these labels on a conversation once it ends, or after the first answer in the portal chat.
        </p>
      </div>
      {other.length > 0 ? <ErrorMessage error={new Error(other.join(' '))} title="Could not save the label" /> : null}
      {save.isError && !(save.error instanceof ApiError && save.error.isValidation) ? (
        <ErrorMessage error={save.error} title="Could not save the label" />
      ) : null}
      <div className="flex justify-end gap-2">
        <Button onClick={onDone} disabled={save.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="primary" loading={save.isPending}>
          {label ? 'Save label' : 'Create label'}
        </Button>
      </div>
    </form>
  );
}

export interface ManageLabelsSheetProps {
  open: boolean;
  onClose: () => void;
}

export function ManageLabelsSheet({ open, onClose }: ManageLabelsSheetProps) {
  const labels = useLabels(open);
  const refresh = useRefreshAfterLabelChange();
  // null: no form; 'new': a new label; otherwise the id being edited.
  const [editing, setEditing] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Label | null>(null);
  const [confirmRelabel, setConfirmRelabel] = useState(false);

  const remove = useMutation({
    mutationFn: (label: Label) => labelsApi.remove(label.id),
    onSuccess: (_, label) => {
      refresh();
      setDeleting(null);
      toast({ title: `Deleted ${label.name}`, tone: 'success' });
    },
  });
  const relabel = useMutation({
    mutationFn: () => labelsApi.relabel(RELABEL_DAYS),
    onSuccess: (queued) => {
      setConfirmRelabel(false);
      toast({
        title: queued === 0 ? 'Nothing to re-label' : `Re-labeling ${pluralize(queued, 'conversation')}`,
        description: queued === 0 ? `No conversations in the last ${RELABEL_DAYS} days.` : 'Labels appear as the assistant goes through them.',
        tone: 'success',
      });
    },
  });

  const all = labelsOrEmpty(labels.data);
  const autoCount = all.filter((label) => label.autoApply).length;

  return (
    <SideSheet
      open={open}
      onClose={onClose}
      title="Labels"
      description="Labels help the team find and count conversations. Staff put them on conversations, and the assistant can too."
      footer={
        <>
          <Button
            icon={RefreshCw}
            onClick={() => setConfirmRelabel(true)}
            disabled={autoCount === 0}
            title={autoCount === 0 ? 'No label applies automatically' : undefined}
          >
            Re-label last {RELABEL_DAYS} days
          </Button>
          <Button variant="primary" onClick={onClose}>
            Done
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {editing === 'new' ? (
          <LabelForm label={null} onDone={() => setEditing(null)} />
        ) : (
          <Button icon={Plus} onClick={() => setEditing('new')}>
            New label
          </Button>
        )}

        {labels.isPending ? (
          <Spinner label="Loading labels" />
        ) : labels.isError ? (
          <ErrorMessage error={labels.error} title="Could not load the labels" />
        ) : all.length === 0 ? (
          <EmptyState icon={Tags} title="No labels yet" description="Create labels such as Question, Problem or Billing to sort conversations." />
        ) : (
          <ul className="divide-y divide-border-subtle rounded-xl border border-border" aria-label="Labels">
            {all.map((label) =>
              editing === label.id ? (
                <li key={label.id} className="p-2">
                  <LabelForm label={label} onDone={() => setEditing(null)} />
                </li>
              ) : (
                <li key={label.id} className="flex items-start gap-3 px-3 py-2.5">
                  <div className="min-w-0 flex-1 space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <LabelChip name={label.name} color={label.color} />
                      {label.autoApply ? (
                        <span className="inline-flex items-center gap-1 text-xs text-fg-muted">
                          <Sparkles className="size-3" aria-hidden />
                          Applies automatically
                        </span>
                      ) : null}
                    </div>
                    {label.description ? <p className="text-small break-words text-fg-secondary">{label.description}</p> : null}
                    <p className="text-xs text-fg-muted">{pluralize(label.conversationCount, 'conversation')}</p>
                  </div>
                  <div className="flex shrink-0 gap-1">
                    <Button size="icon-sm" variant="ghost" icon={Pencil} aria-label={`Edit ${label.name}`} onClick={() => setEditing(label.id)} />
                    <Button size="icon-sm" variant="danger-ghost" icon={Trash2} aria-label={`Delete ${label.name}`} onClick={() => setDeleting(label)} />
                  </div>
                </li>
              ),
            )}
          </ul>
        )}
      </div>

      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.name ?? 'label'}?`}
        message={
          deleting && deleting.conversationCount > 0
            ? `It comes off ${pluralize(deleting.conversationCount, 'conversation')}. The conversations stay.`
            : 'No conversation has it.'
        }
        confirmLabel="Delete label"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} title="Could not delete the label" /> : undefined}
        onConfirm={() => deleting && remove.mutate(deleting)}
        onCancel={() => {
          setDeleting(null);
          remove.reset();
        }}
      />
      <ConfirmDialog
        open={confirmRelabel}
        title={`Re-label the last ${RELABEL_DAYS} days?`}
        message={`The assistant looks again at up to 500 conversations from the last ${RELABEL_DAYS} days and replaces the labels it added. Labels people set stay, and labels people took off stay off.`}
        confirmLabel="Re-label"
        tone="primary"
        busy={relabel.isPending}
        error={relabel.isError ? <ErrorMessage error={relabel.error} title="Could not re-label" /> : undefined}
        onConfirm={() => relabel.mutate()}
        onCancel={() => {
          setConfirmRelabel(false);
          relabel.reset();
        }}
      />
    </SideSheet>
  );
}
