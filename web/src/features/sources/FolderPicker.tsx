import { clsx } from 'clsx';
import { Folder, FolderRoot, Inbox, type LucideIcon } from 'lucide-react';
import { useId } from 'react';
import type { Guid } from '../../lib/types';
import type { FolderNode } from './folders';

export interface FolderPickerProps {
  legend: string;
  nodes: readonly FolderNode[];
  // Null picks the root option, undefined picks nothing yet.
  value: Guid | null | undefined;
  onChange: (folderId: Guid | null) => void;
  rootLabel: string;
  rootKind?: 'top-level' | 'unfiled';
  // A returned reason disables the option and is shown next to it.
  blocker?: (node: FolderNode | null) => string | null;
}

// Radio buttons, so the browser provides arrow-key navigation and screen readers announce the group.
export function FolderPicker({ legend, nodes, value, onChange, rootLabel, rootKind = 'top-level', blocker }: FolderPickerProps) {
  const name = useId();
  const options: Array<{ id: Guid | null; label: string; depth: number; icon: LucideIcon; reason: string | null }> = [
    { id: null, label: rootLabel, depth: 0, icon: rootKind === 'unfiled' ? Inbox : FolderRoot, reason: blocker?.(null) ?? null },
    ...nodes.map((node) => ({ id: node.folder.id, label: node.folder.name, depth: node.depth, icon: Folder, reason: blocker?.(node) ?? null })),
  ];

  return (
    <fieldset className="min-w-0">
      <legend className="mb-1.5 text-small font-medium text-fg">{legend}</legend>
      <div className="max-h-64 overflow-y-auto rounded-xl border border-border p-1">
        {options.map((option) => {
          const Icon = option.icon;
          const checked = value !== undefined && value === option.id;
          const disabled = option.reason !== null;
          return (
            <label
              key={option.id ?? 'root'}
              style={{ paddingLeft: `${0.5 + Math.max(0, option.depth - 1) * 1.25}rem` }}
              className={clsx(
                'flex min-h-8 items-center gap-2 rounded-md py-1 pr-2 text-sm',
                disabled ? 'cursor-not-allowed text-fg-disabled' : checked ? 'cursor-pointer bg-fill-active text-fg' : 'cursor-pointer text-fg hover:bg-fill-hover',
              )}
            >
              <input
                type="radio"
                name={name}
                checked={checked}
                disabled={disabled}
                onChange={() => onChange(option.id)}
                className="size-4 shrink-0"
              />
              <Icon className={clsx('size-4 shrink-0', disabled ? 'text-fg-disabled' : 'text-fg-muted')} aria-hidden />
              <span className="min-w-0 flex-1 truncate">{option.label}</span>
              {option.reason ? <span className="shrink-0 text-xs text-fg-muted">{option.reason}</span> : null}
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
