import { clsx } from 'clsx';
import { ChevronRight, Folder, FolderLock, FolderOpen, FolderPlus, Inbox, Library, type LucideIcon } from 'lucide-react';
import { useCallback, useEffect, useId, useState, type ReactNode } from 'react';
import { Link, type To } from 'react-router';
import { Button } from '../../components/ui/Button';
import { ErrorMessage, Spinner } from '../../components/ui/feedback';
import { formatNumber, pluralize } from '../../lib/format';
import type { Guid } from '../../lib/types';
import { endDrag, startDrag, useDropTarget, type DragPayload, type FolderDrop } from './dragAndDrop';
import { folderPath, INTERNAL_FOLDER_NOTE, isInternalNode, UNFILED, type FolderNode, type FolderView } from './folders';

export interface FolderTreeProps {
  tree: readonly FolderNode[];
  view: FolderView;
  totalCount: number | undefined;
  unfiledCount: number | undefined;
  loading: boolean;
  error: unknown;
  hrefFor: (view: FolderView) => To;
  onNewFolder: () => void;
  drop: FolderDrop;
  onNavigate?: () => void;
}

// Hovering a collapsed folder this long while dragging opens it, as in a file manager.
const SPRING_OPEN_MS = 700;

const isFolder = (payload: DragPayload) => payload.kind === 'folder';
const isSources = (payload: DragPayload) => payload.kind !== 'folder';

// Remembered across visits, so a large tree opens the way it was left.
const EXPANDED_KEY = 'ka.sources.expandedFolders';

function readExpanded(): ReadonlySet<Guid> {
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(EXPANDED_KEY) ?? '[]');
    return new Set(Array.isArray(stored) ? stored.filter((id): id is Guid => typeof id === 'string') : []);
  } catch {
    return new Set();
  }
}

function saveExpanded(expanded: ReadonlySet<Guid>): void {
  try {
    localStorage.setItem(EXPANDED_KEY, JSON.stringify([...expanded]));
  } catch {
    // Storage can be blocked, for example in private windows; the tree then opens collapsed next time.
  }
}

// The open view is filled with a neutral icon, as selected list items are: the accent is kept for the primary action.
function rowClass(current: boolean, dropping: boolean) {
  return clsx(
    'group flex items-center rounded-md',
    dropping
      ? 'bg-accent-subtle text-accent-text ring-1 ring-accent'
      : current
        ? 'bg-fill-active font-medium text-fg'
        : 'text-fg-secondary hover:bg-fill-hover hover:text-fg',
  );
}

function iconClass(current: boolean) {
  return clsx('size-4 shrink-0', current ? 'text-fg' : 'text-fg-muted group-hover:text-fg');
}

function Count({ value }: { value: number | undefined }) {
  if (value === undefined) {
    return null;
  }
  return (
    <>
      <span aria-hidden className="ml-auto shrink-0 pl-2 text-xs font-normal text-fg-muted tabular-nums">
        {formatNumber(value)}
      </span>
      <span className="sr-only">, {pluralize(value, 'source')}</span>
    </>
  );
}

function ViewLink({
  to,
  current,
  icon: Icon,
  label,
  count,
  onNavigate,
}: {
  to: To;
  current: boolean;
  icon: LucideIcon;
  label: ReactNode;
  count: number | undefined;
  onNavigate?: () => void;
}) {
  return (
    <Link
      to={to}
      aria-current={current ? 'page' : undefined}
      onClick={onNavigate}
      className="flex h-8 min-w-0 flex-1 items-center gap-2 rounded-md px-2 text-sm"
    >
      <Icon className={iconClass(current)} strokeWidth={1.75} aria-hidden />
      <span className="truncate">{label}</span>
      <Count value={count} />
    </Link>
  );
}

function UnfiledLink({ props }: { props: FolderTreeProps }) {
  const current = props.view === UNFILED;
  const { dropping, dropHandlers } = useDropTarget(null, props.drop, isSources);
  return (
    <div className={rowClass(current, dropping)} {...dropHandlers}>
      <ViewLink to={props.hrefFor(UNFILED)} current={current} icon={Inbox} label="Unfiled" count={props.unfiledCount} onNavigate={props.onNavigate} />
    </div>
  );
}

function FolderItem({
  node,
  props,
  expanded,
  onToggle,
}: {
  node: FolderNode;
  props: FolderTreeProps;
  expanded: ReadonlySet<Guid>;
  onToggle: (id: Guid, open?: boolean) => void;
}) {
  const listId = useId();
  const { folder, children } = node;
  const current = props.view === folder.id;
  const open = expanded.has(folder.id);
  const { dropping, dropHandlers } = useDropTarget(folder.id, props.drop);
  // The Internal folder stays where it is, so it is not dragged; folders and sources can still be dropped into it.
  const locked = isInternalNode(node);
  const Icon = locked ? FolderLock : current || dropping ? FolderOpen : Folder;

  useEffect(() => {
    if (!dropping || open || children.length === 0) {
      return undefined;
    }
    const timer = setTimeout(() => onToggle(folder.id, true), SPRING_OPEN_MS);
    return () => clearTimeout(timer);
  }, [dropping, open, children.length, folder.id, onToggle]);

  return (
    <li>
      <div
        className={rowClass(current, dropping)}
        draggable={!locked}
        onDragStart={locked ? undefined : (event) => startDrag(event, { kind: 'folder', id: folder.id }, folder.name)}
        onDragEnd={locked ? undefined : endDrag}
        {...dropHandlers}
      >
        {children.length > 0 ? (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={listId}
            aria-label={`Subfolders of ${folder.name}`}
            onClick={() => onToggle(folder.id)}
            className="flex size-7 shrink-0 items-center justify-center rounded-md text-fg-muted hover:text-fg"
          >
            <ChevronRight className={clsx('size-3.5 transition-transform duration-100 ease-out', open && 'rotate-90')} aria-hidden />
          </button>
        ) : (
          <span className="size-7 shrink-0" aria-hidden />
        )}
        <Link
          to={props.hrefFor(folder.id)}
          aria-current={current ? 'page' : undefined}
          onClick={props.onNavigate}
          title={locked ? `${folder.name}: ${INTERNAL_FOLDER_NOTE}` : folder.name}
          aria-description={locked ? INTERNAL_FOLDER_NOTE : undefined}
          className="flex h-8 min-w-0 flex-1 items-center gap-2 rounded-md pr-2 text-sm"
        >
          <Icon className={iconClass(current)} strokeWidth={1.75} aria-hidden />
          <span className="truncate">{folder.name}</span>
          {/* Counted with its subfolders like a folder's size, so a parent that only holds subfolders doesn't read as empty. */}
          <Count value={node.totalCount} />
        </Link>
      </div>
      {children.length > 0 && open ? (
        <ul id={listId} className="mt-0.5 ml-3.5 space-y-0.5 border-l border-border-subtle pl-1">
          {children.map((child) => (
            <FolderItem key={child.folder.id} node={child} props={props} expanded={expanded} onToggle={onToggle} />
          ))}
        </ul>
      ) : null}
    </li>
  );
}

function FoldersHeading({ props, headingId }: { props: FolderTreeProps; headingId: string }) {
  // A folder dropped on the heading moves to the top level.
  const { dropping, dropHandlers } = useDropTarget(null, props.drop, isFolder);
  return (
    <div
      className={clsx('mt-5 mb-1 flex items-center justify-between gap-2 rounded-md pl-2', dropping && 'bg-accent-subtle text-accent-text ring-1 ring-accent')}
      {...dropHandlers}
    >
      <h2 id={headingId} className={clsx('text-xs font-medium', dropping ? 'text-accent-text' : 'text-fg-muted')}>
        {dropping ? 'Move to the top level' : 'Folders'}
      </h2>
      <Button variant="ghost" size="icon-sm" icon={FolderPlus} aria-label="New folder" title="New folder" onClick={props.onNewFolder} />
    </div>
  );
}

export function FolderTree(props: FolderTreeProps) {
  const { tree, view, totalCount, loading, error, hrefFor, onNavigate } = props;
  const headingId = useId();
  const [expanded, setExpanded] = useState<ReadonlySet<Guid>>(readExpanded);

  useEffect(() => saveExpanded(expanded), [expanded]);

  // An opened folder shows its subfolders, and opening one from elsewhere (breadcrumb, subfolder link, URL, a move)
  // reveals it. This happens once per folder and position, so the user can still collapse them afterwards.
  const path = folderPath(tree, view).map((node) => node.folder.id);
  const pathKey = `${view}:${path.join('/')}`;
  const [revealed, setRevealed] = useState('');
  if (revealed !== pathKey) {
    setRevealed(pathKey);
    setExpanded((current) => new Set([...current, ...path]));
  }

  const toggle = useCallback(
    (id: Guid, open?: boolean) =>
      setExpanded((current) => {
        const next = new Set(current);
        if (open ?? !next.has(id)) {
          next.add(id);
        } else {
          next.delete(id);
        }
        return next;
      }),
    [],
  );

  return (
    <nav aria-label="Folders" className="text-sm">
      <ul className="space-y-0.5">
        <li>
          <div className={rowClass(view === 'all', false)}>
            <ViewLink to={hrefFor('all')} current={view === 'all'} icon={Library} label="All sources" count={totalCount} onNavigate={onNavigate} />
          </div>
        </li>
        <li>
          <UnfiledLink props={props} />
        </li>
      </ul>

      <FoldersHeading props={props} headingId={headingId} />
      {loading ? (
        <div className="px-2 py-1">
          <Spinner label="Loading folders" className="size-3.5" />
        </div>
      ) : error ? (
        <ErrorMessage error={error} title="Could not load folders" />
      ) : tree.length === 0 ? (
        <p className="px-2 text-xs text-fg-muted">No folders yet. Create one to group related sources.</p>
      ) : (
        <ul aria-labelledby={headingId} className="space-y-0.5">
          {tree.map((node) => (
            <FolderItem key={node.folder.id} node={node} props={props} expanded={expanded} onToggle={toggle} />
          ))}
        </ul>
      )}
    </nav>
  );
}
