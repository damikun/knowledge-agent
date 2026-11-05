import { clsx } from 'clsx';
import { Check, ListFilter, Pin, X } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { Button } from '../../components/ui/Button';
import { Popover } from '../../components/ui/Popover';
import { formatNumber } from '../../lib/format';
import { LabelDot } from './LabelPicker';
import type { LabelDto } from './types';

export interface ListFilter {
  labelId: string;
  pinned: boolean;
}

export const NO_FILTER: ListFilter = { labelId: '', pinned: false };

type Choice = 'all' | 'pinned' | `label:${string}`;

function choiceOf(filter: ListFilter): Choice {
  return filter.pinned ? 'pinned' : filter.labelId ? `label:${filter.labelId}` : 'all';
}

function filterOf(choice: Choice): ListFilter {
  if (choice === 'pinned') {
    return { labelId: '', pinned: true };
  }
  return choice.startsWith('label:') ? { labelId: choice.slice(6), pinned: false } : NO_FILTER;
}

export function LabelFilter({ value, onChange, labels }: { value: ListFilter; onChange: (filter: ListFilter) => void; labels: readonly LabelDto[] }) {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const name = useId();
  const id = useId();
  const active = value.pinned || value.labelId !== '';
  const choice = choiceOf(value);
  const options: Array<{ value: Choice; label: string; count?: number; color?: LabelDto['color'] }> = [
    { value: 'all', label: 'All conversations' },
    { value: 'pinned', label: 'Pinned' },
    ...labels.map((label) => ({ value: `label:${label.id}` as Choice, label: label.name, count: label.conversationCount, color: label.color })),
  ];

  return (
    <>
      <span className="relative">
        <Button
          ref={buttonRef}
          variant="ghost"
          size="icon-sm"
          icon={ListFilter}
          aria-label={active ? 'Filter conversations (filtered)' : 'Filter conversations'}
          aria-expanded={open}
          aria-haspopup="dialog"
          aria-controls={open ? id : undefined}
          title="Filter by label"
          onClick={() => setOpen((current) => !current)}
          className={clsx(active && 'bg-fill-active text-fg')}
        />
        {active ? <span aria-hidden className="pointer-events-none absolute top-1 right-1 size-1.5 rounded-full bg-accent" /> : null}
      </span>
      <Popover id={id} open={open} onClose={() => setOpen(false)} anchorRef={buttonRef} label="Filter conversations" align="end" className="w-60 p-1">
        <div role="radiogroup" aria-label="Show" className="max-h-80 overflow-y-auto">
          {options.map((option, index) => (
            <label
              key={option.value}
              className={clsx(
                'flex min-h-8 cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm text-fg hover:bg-fill-hover has-focus-visible:bg-fill-hover',
                index === 2 && 'mt-1 border-t border-border-subtle pt-2',
              )}
            >
              <input
                type="radio"
                name={name}
                className="sr-only"
                checked={choice === option.value}
                onChange={() => {
                  onChange(filterOf(option.value));
                  setOpen(false);
                }}
              />
              {option.color ? (
                <LabelDot color={option.color} />
              ) : option.value === 'pinned' ? (
                <Pin className="size-3.5 text-fg-muted" aria-hidden />
              ) : (
                <ListFilter className="size-3.5 text-fg-muted" aria-hidden />
              )}
              <span className="min-w-0 flex-1 truncate">{option.label}</span>
              {option.count !== undefined ? <span className="text-xs text-fg-muted tabular-nums">{formatNumber(option.count)}</span> : null}
              <Check className={clsx('size-3.5 shrink-0 text-fg', choice === option.value ? 'visible' : 'invisible')} aria-hidden />
            </label>
          ))}
        </div>
      </Popover>
    </>
  );
}

export function ActiveFilterChip({ value, labels, onClear }: { value: ListFilter; labels: readonly LabelDto[]; onClear: () => void }) {
  if (!value.pinned && !value.labelId) {
    return null;
  }
  const label = labels.find((l) => l.id === value.labelId);
  const text = value.pinned ? 'Pinned' : (label?.name ?? 'Label');
  return (
    <div className="flex items-center gap-1.5 px-3 pb-2">
      <span className="inline-flex h-6 max-w-full items-center gap-1.5 rounded-full border border-border pr-0.5 pl-2 text-xs font-medium text-fg-secondary">
        {label ? <LabelDot color={label.color} /> : <Pin className="size-3" aria-hidden />}
        <span className="truncate">{text}</span>
        <button
          type="button"
          onClick={onClear}
          aria-label={`Remove filter ${text}`}
          className="flex size-5 items-center justify-center rounded-full text-fg-muted hover:bg-fill-hover hover:text-fg"
        >
          <X className="size-3" aria-hidden />
        </button>
      </span>
    </div>
  );
}
