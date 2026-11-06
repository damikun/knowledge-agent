import { clsx } from 'clsx';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { fieldAria } from '../../components/ui/aria';
import { Button } from '../../components/ui/Button';
import { Field } from '../../components/ui/Field';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Menu } from '../../components/ui/Menu';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { Select } from '../../components/ui/Select';
import { Switch } from '../../components/ui/Switch';
import { toast } from '../../components/ui/Toast';
import { ApiError } from '../../lib/api';
import { formatNumber } from '../../lib/format';
import { useDeleteSegment, usePeopleCount, useSaveSegment, useSegments } from './api';
import { BUILT_IN_SEGMENTS } from './filters';
import type { ContactCondition, Segment } from './types';

const COLORS = ['gray', 'blue', 'green', 'amber', 'red', 'purple', 'teal', 'pink'] as const;
const COLOR_OPTIONS = COLORS.map((color) => ({ value: color, label: color[0]!.toUpperCase() + color.slice(1) }));

const DOT: Record<string, string> = {
  gray: 'bg-tag-gray',
  blue: 'bg-tag-blue',
  green: 'bg-tag-green',
  amber: 'bg-tag-amber',
  red: 'bg-tag-red',
  purple: 'bg-tag-purple',
  teal: 'bg-tag-teal',
  pink: 'bg-tag-pink',
};

export interface SegmentListProps {
  /** A built-in id (all, identified, returning, new) or a saved segment's id. */
  value: string;
  onChange: (id: string) => void;
  admin: boolean;
  onEdit: (segment: Segment) => void;
  /** Saves the current filter, or opens the filter builder when there is none. */
  onNew: () => void;
  className?: string;
}

function Count({ value, className }: { value: number; className?: string }) {
  return <span className={clsx('shrink-0 text-xs text-fg-muted tabular-nums', className)}>{formatNumber(value)}</span>;
}

// Counts the built-in view regardless of the list's search and filters.
function BuiltInCount({ filter }: { filter: readonly ContactCondition[] }) {
  const count = usePeopleCount({ q: '', widgetId: '', identified: '', conditions: [...filter], segmentId: '' });
  return count.data === undefined ? null : <Count value={count.data} />;
}

export function SegmentList({ value, onChange, admin, onEdit, onNew, className }: SegmentListProps) {
  const segments = useSegments();
  const remove = useDeleteSegment();
  const [deleting, setDeleting] = useState<Segment | null>(null);
  const item = (id: string, name: string, extra: { count?: ReactNode; color?: string; segment?: Segment; title?: string }) => (
    <li key={id} className="group relative">
      <button
        type="button"
        aria-current={value === id ? 'true' : undefined}
        title={extra.title}
        onClick={() => onChange(id)}
        className={clsx(
          'flex h-8 w-full min-w-0 items-center gap-2 rounded-md px-2 text-left text-small',
          extra.segment && 'pointer-coarse:pr-9',
          value === id ? 'bg-fill-active font-medium text-fg' : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
        )}
      >
        {extra.color ? <span className={clsx('size-2 shrink-0 rounded-full', DOT[extra.color] ?? DOT.gray)} aria-hidden /> : null}
        <span className="min-w-0 flex-1 truncate">{name}</span>
        {extra.count}
      </button>
      {extra.segment ? (
        <div className="absolute top-0.5 right-0.5 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">
          <Menu
            label={`Actions for ${extra.segment.name}`}
            items={[
              { label: 'Edit', icon: Pencil, onSelect: () => onEdit(extra.segment!) },
              ...(admin ? [{ label: 'Delete', icon: Trash2, danger: true, onSelect: () => setDeleting(extra.segment!) }] : []),
            ]}
          />
        </div>
      ) : null}
    </li>
  );

  return (
    <nav aria-label="Segments" className={clsx('space-y-5', className)}>
      <ul className="space-y-0.5">
        {BUILT_IN_SEGMENTS.map((segment) => item(segment.id, segment.name, { count: <BuiltInCount filter={segment.filter} /> }))}
      </ul>
      <div className="space-y-0.5">
        <p className="flex h-7 items-center px-2 text-xs font-medium text-fg-muted">Segments</p>
        {segments.isPending ? (
          <Spinner label="Loading segments" className="px-2 py-1" />
        ) : segments.isError ? (
          <p className="px-2 py-1 text-xs text-fg-muted">Segments could not be loaded.</p>
        ) : segments.data.length === 0 ? (
          <p className="px-2 py-1 text-xs text-fg-muted">Save a filtered list to see it here.</p>
        ) : (
          <ul className="space-y-0.5">
            {segments.data.map((segment) =>
              item(segment.id, segment.name, {
                // The menu replaces the count on hover and focus; on touch it sits beside it.
                count: <Count value={segment.count} className="group-focus-within:invisible group-hover:invisible pointer-coarse:visible" />,
                color: segment.color,
                segment,
                ...(segment.description ? { title: segment.description } : {}),
              }),
            )}
          </ul>
        )}
        <button
          type="button"
          onClick={onNew}
          className="flex h-8 w-full items-center gap-2 rounded-md px-2 text-left text-small text-fg-muted hover:bg-fill-hover hover:text-fg"
        >
          <Plus className="size-4 shrink-0" aria-hidden />
          New segment
        </button>
      </div>
      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.name ?? 'segment'}?`}
        message="The segment goes for everyone who uses it. The contacts stay."
        confirmLabel="Delete segment"
        busy={remove.isPending}
        error={remove.error ? <ErrorMessage error={remove.error} /> : null}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          const target = deleting;
          if (!target) {
            return;
          }
          remove.mutate(target.id, {
            onSuccess: () => {
              toast({ title: `Deleted ${target.name}`, tone: 'success' });
              setDeleting(null);
              if (value === target.id) {
                onChange('all');
              }
            },
          });
        }}
      />
    </nav>
  );
}

export interface SegmentDialogProps {
  open: boolean;
  onClose: () => void;
  /** Null saves the filter as a new segment. */
  segment: Segment | null;
  filter: ContactCondition[];
  onSaved: (segment: Segment) => void;
}

export function SegmentDialog({ open, onClose, segment, filter, onSaved }: SegmentDialogProps) {
  const id = useId();
  const save = useSaveSegment();
  // Mounted per opening, so each starts from the segment it edits.
  const [name, setName] = useState(segment?.name ?? '');
  const [description, setDescription] = useState(segment?.description ?? '');
  const [color, setColor] = useState<string>(segment?.color ?? 'gray');
  const [shared, setShared] = useState(segment?.shared ?? true);

  const errors = (field: string) =>
    save.error instanceof ApiError ? Object.entries(save.error.fieldErrors ?? {}).find(([key]) => key.toLowerCase() === field)?.[1] : undefined;

  const submit = () =>
    save.mutate(
      { ...(segment ? { id: segment.id } : {}), body: { name: name.trim(), description: description.trim(), color, shared, filter } },
      {
        onSuccess: (saved) => {
          toast({ title: segment ? 'Segment saved' : `Saved ${saved.name}`, tone: 'success' });
          onSaved(saved);
          onClose();
        },
      },
    );

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={segment ? `Edit ${segment.name}` : 'Save as segment'}
      size="sm"
      dismissible={!save.isPending}
      footer={
        <>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button variant="primary" disabled={name.trim() === ''} loading={save.isPending} onClick={submit}>
            {segment ? 'Save' : 'Save segment'}
          </Button>
        </>
      }
    >
      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          if (name.trim()) {
            submit();
          }
        }}
      >
        <p className="text-small text-fg-secondary">
          A segment is a saved filter: whoever matches it now is in it, so it stays up to date as contacts change.
          {segment ? ' Saving keeps the current filter of the list.' : ''}
        </p>
        <Field label="Name" htmlFor={`${id}-name`} errors={errors('name')}>
          <input
            {...fieldAria(`${id}-name`, errors('name'))}
            className="field"
            value={name}
            maxLength={80}
            autoFocus
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label="Description" htmlFor={`${id}-description`} optional errors={errors('description')}>
          <input
            {...fieldAria(`${id}-description`, errors('description'))}
            className="field"
            value={description}
            maxLength={300}
            onChange={(event) => setDescription(event.target.value)}
          />
        </Field>
        <Field label="Color" htmlFor={`${id}-color`}>
          <Select id={`${id}-color`} value={color} options={COLOR_OPTIONS} onChange={setColor} />
        </Field>
        <Switch checked={shared} onChange={setShared} label="Shared with the team" disabled={segment ? !segment.mine : false} />
        {errors('filter') ? <ErrorMessage error={save.error} title="The filter cannot be saved" /> : null}
        {save.error && !errors('name') && !errors('filter') && !errors('description') ? (
          <ErrorMessage error={save.error} title="The segment could not be saved" />
        ) : null}
        <button type="submit" hidden />
      </form>
    </Modal>
  );
}
