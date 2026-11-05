import { clsx } from 'clsx';
import {
  ChevronRight,
  Folder,
  FolderInput,
  FolderLock,
  FolderPlus,
  FolderTree as FolderTreeIcon,
  PanelLeftClose,
  PanelLeftOpen,
  Pencil,
  Trash,
} from 'lucide-react';
import type { ReactNode } from 'react';
import { Link, type To } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Menu } from '../../components/ui/Menu';
import { formatNumber, pluralize } from '../../lib/format';
import type { Guid } from '../../lib/types';
import { endDrag, startDrag, useDropTarget, type DragPayload, type FolderDrop } from './dragAndDrop';
import {
  folderPath,
  INTERNAL_FOLDER_NOTE,
  isInternalNode,
  MAX_FOLDER_DEPTH,
  newFolderBlocker,
  UNFILED,
  type FolderNode,
  type FolderView,
} from './folders';

export interface LibraryFolderBarProps {
  view: FolderView;
  node: FolderNode | undefined;
  loading: boolean;
  tree: readonly FolderNode[];
  hrefFor: (view: FolderView) => To;
  drop: FolderDrop;
  /** Opens the folders in a drawer, below the width where they sit beside the list. */
  onShowFolders: () => void;
  /** Whether the folder panel beside the list is hidden; it hides and shows from here. */
  foldersHidden: boolean;
  onToggleFolders: () => void;
  /** At the end of the row, such as the search field. */
  end?: ReactNode;
  onNewSubfolder: (node: FolderNode) => void;
  onRename: (node: FolderNode) => void;
  onMove: (node: FolderNode) => void;
  onDelete: (node: FolderNode) => void;
}

const isFolder = (payload: DragPayload) => payload.kind === 'folder';

function Crumb({
  to,
  folderId,
  drop,
  accepts,
  children,
}: {
  to: To;
  folderId: Guid | null;
  drop: FolderDrop;
  accepts?: (payload: DragPayload) => boolean;
  children: ReactNode;
}) {
  const { dropping, dropHandlers } = useDropTarget(folderId, drop, accepts);
  return (
    <li className="flex min-w-0 items-center gap-0.5">
      <Link
        to={to}
        className={clsx(
          'truncate rounded-sm px-1 text-fg-secondary underline-offset-2 hover:text-fg hover:underline',
          dropping && 'bg-accent-subtle text-accent-text ring-1 ring-accent',
        )}
        {...dropHandlers}
      >
        {children}
      </Link>
      <ChevronRight className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
    </li>
  );
}

function SubfolderLink({ node, to, drop }: { node: FolderNode; to: To; drop: FolderDrop }) {
  const { dropping, dropHandlers } = useDropTarget(node.folder.id, drop);
  return (
    <li>
      <Link
        to={to}
        draggable
        onDragStart={(event) => startDrag(event, { kind: 'folder', id: node.folder.id }, node.folder.name)}
        onDragEnd={endDrag}
        className={clsx(
          'inline-flex h-7 max-w-64 items-center gap-1.5 rounded-full border px-2.5',
          dropping ? 'bg-accent-subtle text-accent-text ring-1 ring-accent' : 'border-border text-fg-secondary hover:bg-fill-hover hover:text-fg',
        )}
        {...dropHandlers}
      >
        <Folder className="size-3.5 shrink-0 text-fg-muted" aria-hidden />
        <span className="truncate">{node.folder.name}</span>
        <span aria-hidden className="text-fg-muted tabular-nums">
          {formatNumber(node.totalCount)}
        </span>
        <span className="sr-only">, {pluralize(node.totalCount, 'source')} including its subfolders</span>
      </Link>
    </li>
  );
}

export function LibraryFolderBar({
  view,
  node,
  loading,
  tree,
  hrefFor,
  drop,
  onShowFolders,
  onNewSubfolder,
  onRename,
  onMove,
  onDelete,
  foldersHidden,
  onToggleFolders,
  end,
}: LibraryFolderBarProps) {
  const path = node ? folderPath(tree, node.folder.id) : [];
  const current = node?.folder.name ?? (view === UNFILED ? 'Unfiled' : view === 'all' ? 'All sources' : loading ? 'Loading folder…' : 'Folder not found');
  const blocker = node ? newFolderBlocker(node) : null;
  const locked = node ? isInternalNode(node) : false;

  return (
    <div className="space-y-2">
      <div className="flex min-h-7 flex-wrap items-center gap-x-2 gap-y-2">
        <Button size="sm" icon={FolderTreeIcon} onClick={onShowFolders} className="lg:hidden">
          Folders
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          icon={foldersHidden ? PanelLeftOpen : PanelLeftClose}
          aria-label={foldersHidden ? 'Show folders' : 'Hide folders'}
          aria-expanded={!foldersHidden}
          aria-controls="source-folders"
          title={foldersHidden ? 'Show folders' : 'Hide folders'}
          onClick={onToggleFolders}
          className="-ml-1.5 max-lg:hidden"
        />
        <nav aria-label="Breadcrumb" className={clsx('min-w-0', view === 'all' ? 'sr-only' : 'flex-1')}>
          <ol className="flex min-w-0 flex-wrap items-center gap-0.5 text-sm">
            {view !== 'all' ? (
              // A folder dropped on "All sources" moves to the top level.
              <Crumb to={hrefFor('all')} folderId={null} drop={drop} accepts={isFolder}>
                All sources
              </Crumb>
            ) : null}
            {path.slice(0, -1).map((ancestor) => (
              <Crumb key={ancestor.folder.id} to={hrefFor(ancestor.folder.id)} folderId={ancestor.folder.id} drop={drop}>
                {ancestor.folder.name}
              </Crumb>
            ))}
            {/* The page title already says Sources; the bar names a folder only once one is open. */}
            <li className="min-w-0 px-1">
              <span aria-current="page" className="block truncate font-medium text-fg">
                {current}
              </span>
            </li>
          </ol>
        </nav>
        {node ? (
          <div className="flex items-center gap-1">
            <Button
              size="sm"
              variant="ghost"
              icon={FolderPlus}
              disabled={blocker !== null}
              title={blocker ? `Folders nest at most ${MAX_FOLDER_DEPTH} levels deep` : undefined}
              onClick={() => onNewSubfolder(node)}
            >
              New subfolder
            </Button>
            {/* Rename, move and delete are rare, so they wait in a menu. The Internal folder is built in and has none. */}
            {locked ? null : (
              <Menu
                label="Folder actions"
                items={[
                  { label: 'Rename', icon: Pencil, onSelect: () => onRename(node) },
                  { label: 'Move', icon: FolderInput, onSelect: () => onMove(node) },
                  { label: 'Delete', icon: Trash, danger: true, onSelect: () => onDelete(node) },
                ]}
              />
            )}
          </div>
        ) : null}
        {end}
      </div>
      {locked ? (
        <p data-testid="internal-folder-note" className="flex items-center gap-1.5 text-xs text-fg-muted">
          <FolderLock className="size-3.5 shrink-0" aria-hidden />
          {INTERNAL_FOLDER_NOTE}
        </p>
      ) : null}
      {node && node.children.length > 0 ? (
        <div className="flex flex-wrap items-center gap-1.5 text-xs">
          <span className="text-fg-muted">Subfolders</span>
          <ul className="flex flex-wrap gap-1.5">
            {node.children.map((child) => (
              <SubfolderLink key={child.folder.id} node={child} to={hrefFor(child.folder.id)} drop={drop} />
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
