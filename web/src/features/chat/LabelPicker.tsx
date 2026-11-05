import { clsx } from 'clsx';
import { Check, Search, Sparkles } from 'lucide-react';
import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import { Popover } from '../../components/ui/Popover';
import { labelStyle } from './labelColors';
import type { ConversationLabelDto, LabelColor, LabelDto } from './types';

export function LabelDot({ color, className }: { color: LabelColor; className?: string }) {
  return <span aria-hidden className={clsx('size-2 shrink-0 rounded-full forced-colors:border', labelStyle(color).solid, className)} />;
}

export function LabelChip({ label }: { label: ConversationLabelDto }) {
  const style = labelStyle(label.color);
  return (
    <span
      className={clsx('inline-flex h-5 max-w-40 items-center gap-1 rounded-full px-2 text-xs font-medium', style.subtle, style.text)}
      title={label.source === 'auto' ? `${label.name}: added automatically` : label.name}
    >
      <LabelDot color={label.color} className="size-1.5" />
      <span className="truncate">{label.name}</span>
      {label.source === 'auto' ? (
        <>
          <Sparkles className="size-3 shrink-0 opacity-70" aria-hidden />
          <span className="sr-only">, added automatically</span>
        </>
      ) : null}
    </span>
  );
}

export interface LabelPickerProps {
  open: boolean;
  onClose: () => void;
  anchorRef?: RefObject<HTMLElement | null>;
  anchor?: HTMLElement | null;
  labels: readonly LabelDto[];
  value: readonly ConversationLabelDto[];
  onChange: (labels: LabelDto[]) => void;
}

/** Each tick saves at once. */
export function LabelPicker({ open, onClose, anchorRef, anchor, labels, value, onChange }: LabelPickerProps) {
  const [query, setQuery] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);
  const searchId = useId();
  // After the menu that opened the picker has handed focus back to its button.
  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const timer = window.setTimeout(() => searchRef.current?.focus(), 0);
    return () => window.clearTimeout(timer);
  }, [open]);
  const current = new Map(value.map((label) => [label.id, label] as const));
  const q = query.trim().toLowerCase();
  const shown = labels.filter((label) => !q || label.name.toLowerCase().includes(q));

  const toggle = (label: LabelDto) => {
    const next = current.has(label.id) ? labels.filter((l) => current.has(l.id) && l.id !== label.id) : labels.filter((l) => current.has(l.id) || l.id === label.id);
    onChange(next);
  };

  return (
    <Popover
      open={open}
      onClose={() => {
        setQuery('');
        onClose();
      }}
      anchorRef={anchorRef}
      anchor={anchor}
      label="Labels"
      align="end"
      className="w-64 p-1"
      initialFocus={searchRef}
    >
      <div className="relative px-1 pt-1 pb-1.5">
        <label htmlFor={searchId} className="sr-only">
          Search labels
        </label>
        <Search className="pointer-events-none absolute top-1/2 left-3.5 size-3.5 -translate-y-1/2 text-fg-muted" aria-hidden />
        <input
          ref={searchRef}
          id={searchId}
          type="text"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search labels"
          className="h-8 w-full rounded-md border border-border bg-surface pr-2 pl-7 text-sm text-fg placeholder:text-fg-muted focus:border-focus focus:ring-1 focus:ring-focus focus:outline-none"
        />
      </div>
      {labels.length === 0 ? (
        <p className="px-2.5 py-2 text-small text-fg-muted">No labels yet. Labels are created by an admin.</p>
      ) : shown.length === 0 ? (
        <p className="px-2.5 py-2 text-small text-fg-muted">No labels match “{query.trim()}”.</p>
      ) : (
        <ul className="max-h-64 overflow-y-auto" aria-label="Labels">
          {shown.map((label) => {
            const applied = current.get(label.id);
            return (
              <li key={label.id}>
                <label className="flex min-h-8 cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm text-fg hover:bg-fill-hover has-focus-visible:bg-fill-hover">
                  <input type="checkbox" className="peer sr-only" checked={applied !== undefined} onChange={() => toggle(label)} />
                  <span
                    aria-hidden
                    className="flex size-4 shrink-0 items-center justify-center rounded-sm border border-border-strong peer-checked:border-control peer-checked:bg-control peer-checked:text-fg-on-control peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-focus"
                  >
                    {applied ? <Check className="size-3" /> : null}
                  </span>
                  <LabelDot color={label.color} />
                  <span className="min-w-0 flex-1 truncate">{label.name}</span>
                  {applied?.source === 'auto' ? (
                    <span title="Added automatically" className="text-fg-muted">
                      <Sparkles className="size-3.5" aria-hidden />
                      <span className="sr-only">Added automatically</span>
                    </span>
                  ) : null}
                </label>
              </li>
            );
          })}
        </ul>
      )}
    </Popover>
  );
}
