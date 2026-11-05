import { clsx } from 'clsx';
import { Ellipsis, Pencil, Pin, PinOff, Sparkles, Tag, Trash2 } from 'lucide-react';
import { memo, useRef, type ReactNode } from 'react';
import { Link } from 'react-router';
import { Menu, type MenuItem } from '../../components/ui/Menu';
import { formatDateTime } from '../../lib/format';
import { mergeRefs } from '../../lib/mergeRefs';
import { formatRowTime } from './conversationGroups';
import { LabelDot } from './LabelPicker';
import { TitleInput } from './TitleInput';
import type { ConversationLabelDto, ConversationListItem } from './types';

function highlight(text: string, query: string): ReactNode {
  const q = query.trim().toLowerCase();
  if (!q) {
    return text;
  }
  const lower = text.toLowerCase();
  const parts: ReactNode[] = [];
  let from = 0;
  let at = lower.indexOf(q);
  while (at !== -1) {
    if (at > from) {
      parts.push(text.slice(from, at));
    }
    parts.push(
      <mark key={at} className="rounded-xs bg-accent-subtle text-accent-text">
        {text.slice(at, at + q.length)}
      </mark>,
    );
    from = at + q.length;
    at = lower.indexOf(q, from);
  }
  if (from < text.length) {
    parts.push(text.slice(from));
  }
  return parts;
}

const MAX_DOTS = 3;

function LabelDots({ labels }: { labels: readonly ConversationLabelDto[] }) {
  if (labels.length === 0) {
    return null;
  }
  const names = labels.map((label) => (label.source === 'auto' ? `${label.name} (added automatically)` : label.name)).join(', ');
  return (
    <span className="flex shrink-0 items-center gap-0.5" title={names}>
      {labels.slice(0, MAX_DOTS).map((label) => (
        <LabelDot key={label.id} color={label.color} className="size-1.5" />
      ))}
      {labels.some((label) => label.source === 'auto') ? <Sparkles className="size-2.5 text-fg-muted" aria-hidden /> : null}
      <span className="sr-only">, labels: {names}</span>
    </span>
  );
}

export interface ConversationRowProps {
  item: ConversationListItem;
  basePath: string;
  active: boolean;
  /** The row Tab lands on; the others are reached with the arrow keys. */
  tabbable: boolean;
  query: string;
  editing: boolean;
  labelsAvailable: boolean;
  onNavigate?: () => void;
  onFocusRow: (id: string) => void;
  onStartRename: (id: string) => void;
  onEndRename: (id: string) => void;
  onRename: (item: ConversationListItem, title: string) => void;
  onTogglePin: (item: ConversationListItem) => void;
  onEditLabels: (item: ConversationListItem, anchor: HTMLElement) => void;
  onDelete: (item: ConversationListItem) => void;
}

export const ConversationRow = memo(function ConversationRow({
  item,
  basePath,
  active,
  tabbable,
  query,
  editing,
  labelsAvailable,
  onNavigate,
  onFocusRow,
  onStartRename,
  onEndRename,
  onRename,
  onTogglePin,
  onEditLabels,
  onDelete,
}: ConversationRowProps) {
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const title = item.title || 'New conversation';

  if (editing) {
    return (
      <li className="px-0.5 py-0.5">
        <TitleInput
          title={item.title}
          className="w-full"
          onDone={(next) => {
            if (next !== null) {
              onRename(item, next);
            }
            onEndRename(item.id);
          }}
        />
      </li>
    );
  }

  const items: MenuItem[] = [
    { label: 'Rename', icon: Pencil, onSelect: () => onStartRename(item.id) },
    { label: item.pinned ? 'Unpin' : 'Pin', icon: item.pinned ? PinOff : Pin, onSelect: () => onTogglePin(item) },
    ...(labelsAvailable ? [{ label: 'Labels', icon: Tag, onSelect: () => triggerRef.current && onEditLabels(item, triggerRef.current) }] : []),
    { label: 'Delete', icon: Trash2, danger: true, onSelect: () => onDelete(item) },
  ];

  return (
    // content-visibility skips layout for rows far off screen, so thousands of rows stay cheap without virtualizing.
    <li className="group/row relative [contain-intrinsic-size:auto_2rem] [content-visibility:auto]">
      <Link
        to={`${basePath}/${item.id}`}
        data-row-id={item.id}
        tabIndex={tabbable ? 0 : -1}
        onClick={onNavigate}
        onFocus={() => onFocusRow(item.id)}
        aria-current={active ? 'page' : undefined}
        title={title}
        className={clsx(
          'flex h-8 items-center gap-2 rounded-md pr-13 pl-2.5 text-sm',
          active ? 'bg-fill-active font-medium text-fg' : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
        )}
      >
        <span className="min-w-0 flex-1 truncate">{highlight(title, query)}</span>
        <LabelDots labels={item.labels} />
      </Link>
      <span
        className="pointer-events-none absolute top-1/2 right-2 -translate-y-1/2 text-xs text-fg-muted tabular-nums group-focus-within/row:invisible group-hover/row:invisible max-md:hidden"
        title={formatDateTime(item.updatedAt)}
        aria-hidden
      >
        {formatRowTime(item.updatedAt)}
      </span>
      <div className="absolute top-1/2 right-0.5 -translate-y-1/2 opacity-0 group-focus-within/row:opacity-100 group-hover/row:opacity-100 has-[[aria-expanded=true]]:opacity-100 max-md:opacity-100">
        <Menu
          label={`Actions for “${title}”`}
          items={items}
          trigger={(props) => (
            <button
              {...props}
              ref={mergeRefs(props.ref, triggerRef)}
              type="button"
              tabIndex={tabbable ? 0 : -1}
              aria-label={`Actions for “${title}”`}
              title="More actions"
              className="flex size-7 items-center justify-center rounded-md text-fg-muted hover:bg-fill-hover hover:text-fg"
            >
              <Ellipsis className="size-4" aria-hidden />
            </button>
          )}
        />
      </div>
    </li>
  );
});
