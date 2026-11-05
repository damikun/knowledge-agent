import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Check, Plus, Search, Tag } from 'lucide-react';
import { useId, useMemo, useRef, useState } from 'react';
import { Button } from '../../components/ui/Button';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { Popover } from '../../components/ui/Popover';
import type { Guid } from '../../lib/types';
import { labelsApi, labelsOrEmpty, useLabels, useRefreshAfterLabelChange, useSetConversationLabels } from './api';
import { LabelChip } from './LabelChip';
import { LABEL_COLORS, type ConversationLabel, type LabelColor } from './types';

export interface LabelPickerProps {
  conversationId: Guid;
  value: readonly ConversationLabel[];
  /** Admins may create a label from the search words. */
  canCreate: boolean;
  align?: 'start' | 'end';
}

// Rotate colors so quickly created labels don't all look alike.
function nextColor(count: number): LabelColor {
  return LABEL_COLORS[(count + 1) % LABEL_COLORS.length] ?? 'gray';
}

/** Changes save at once. Removing an auto label keeps it off for good. */
export function LabelPicker({ conversationId, value, canCreate, align = 'start' }: LabelPickerProps) {
  const ids = useId();
  const anchorRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  // Optimistic selection while the saves catch up.
  const [selected, setSelected] = useState<Set<Guid> | null>(null);
  const labels = useLabels(open);
  const all = labelsOrEmpty(labels.data);
  const save = useSetConversationLabels(conversationId);
  const refresh = useRefreshAfterLabelChange();

  const current = selected ?? new Set(value.map((label) => label.id));
  const sources = new Map(value.map((label) => [label.id, label.source] as const));
  const words = query.trim();
  const shown = useMemo(() => all.filter((label) => label.name.toLowerCase().includes(words.toLowerCase())), [all, words]);
  const exact = all.some((label) => label.name.trim().toLowerCase() === words.toLowerCase());

  const apply = (next: Set<Guid>) => {
    setSelected(next);
    save.mutate([...next]);
  };

  const toggle = (id: Guid) => {
    const next = new Set(current);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    apply(next);
  };

  const create = useMutation({
    mutationFn: () =>
      labelsApi.create({
        name: words,
        color: nextColor(all.length),
        description: '',
        autoApply: false,
      }),
    onSuccess: (label) => {
      refresh();
      setQuery('');
      apply(new Set([...current, label.id]));
    },
  });

  const close = () => {
    setOpen(false);
    setQuery('');
    setSelected(null);
    create.reset();
  };

  return (
    <>
      <Button
        ref={anchorRef}
        size="sm"
        variant="ghost"
        icon={Tag}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? `${ids}-popover` : undefined}
        onClick={() => (open ? close() : setOpen(true))}
      >
        {value.length > 0 ? 'Edit labels' : 'Add labels'}
      </Button>
      <Popover
        open={open}
        onClose={close}
        anchorRef={anchorRef}
        label="Labels"
        align={align}
        id={`${ids}-popover`}
        initialFocus={searchRef}
        className="w-72 p-2"
      >
        <div className="relative">
          <label htmlFor={`${ids}-search`} className="sr-only">
            Find a label
          </label>
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
          <input
            ref={searchRef}
            id={`${ids}-search`}
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && canCreate && words && !exact && shown.length === 0) {
                event.preventDefault();
                create.mutate();
              }
            }}
            placeholder={canCreate ? 'Find or create a label' : 'Find a label'}
            maxLength={40}
            autoComplete="off"
            className="field h-8 pl-8 text-small"
          />
        </div>
        <div className="mt-2 max-h-64 overflow-y-auto">
          {labels.isPending ? (
            <div className="p-2">
              <Spinner label="Loading labels" />
            </div>
          ) : labels.isError ? (
            <ErrorMessage error={labels.error} title="Could not load the labels" />
          ) : (
            <ul aria-label="Labels" className="space-y-0.5">
              {shown.map((label) => {
                const checked = current.has(label.id);
                return (
                  <li key={label.id}>
                    <label
                      className={clsx(
                        'flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 hover:bg-fill-hover has-focus-visible:outline-2 has-focus-visible:outline-offset-[-2px] has-focus-visible:outline-focus',
                      )}
                    >
                      <input type="checkbox" className="sr-only" checked={checked} onChange={() => toggle(label.id)} />
                      <span
                        aria-hidden
                        className={clsx(
                          'flex size-4 shrink-0 items-center justify-center rounded-sm border',
                          checked ? 'border-control bg-control text-fg-on-control' : 'border-border-strong bg-surface',
                        )}
                      >
                        {checked ? <Check className="size-3" /> : null}
                      </span>
                      <LabelChip name={label.name} color={label.color} />
                      {checked && sources.get(label.id) === 'auto' ? <span className="ml-auto text-xs text-fg-muted">Auto</span> : null}
                    </label>
                  </li>
                );
              })}
              {shown.length === 0 && !(canCreate && words) ? (
                <li className="px-2 py-1.5 text-small text-fg-muted">
                  {all.length === 0
                    ? canCreate
                      ? 'No labels yet. Type a name to create one.'
                      : 'No labels yet. An admin can create them.'
                    : 'No label matches.'}
                </li>
              ) : null}
            </ul>
          )}
          {canCreate && words && !exact && labels.isSuccess && labels.data ? (
            <Button size="sm" variant="ghost" icon={Plus} loading={create.isPending} onClick={() => create.mutate()} className="mt-1 w-full justify-start">
              <span className="truncate">Create label “{words}”</span>
            </Button>
          ) : null}
        </div>
        {create.isError ? <ErrorMessage error={create.error} title="Could not create the label" className="mt-2" /> : null}
        {save.isError ? <ErrorMessage error={save.error} title="Could not save the labels" className="mt-2" /> : null}
        <p className="mt-2 border-t border-border-subtle px-2 pt-2 text-xs text-fg-muted">Labels you take off that were added automatically stay off.</p>
      </Popover>
    </>
  );
}
