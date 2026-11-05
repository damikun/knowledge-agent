import { FilterX, FolderOpen, FolderX, Inbox, Library, Plus, SearchX } from 'lucide-react';
import { Button } from '../../components/ui/Button';
import { EmptyState } from '../../components/ui/feedback';
import { Skeleton } from '../../components/ui/Skeleton';
import { pluralize } from '../../lib/format';
import { SOURCE_KINDS, type AddSourceKind } from './AddSourceSheet';
import type { FolderNode } from './folders';

function EmptyLibrary({ onAdd }: { onAdd: (kind: AddSourceKind) => void }) {
  return (
    <EmptyState
      icon={Library}
      title="Add your first source"
      description="Agents answer from what you add here: a website, files or text."
      action={
        <div className="grid gap-2 sm:grid-cols-3">
          {SOURCE_KINDS.map(({ value, label, description, icon: Icon }) => (
            <button
              key={value}
              type="button"
              onClick={() => onAdd(value)}
              className="flex flex-col items-start gap-2 rounded-xl border border-border bg-surface p-3 text-left hover:border-border-hover"
            >
              <Icon className="size-5 text-fg-muted" aria-hidden />
              <span>
                <span className="block text-sm font-medium text-fg">{label}</span>
                <span className="mt-0.5 block text-xs text-fg-secondary">{description}</span>
              </span>
            </button>
          ))}
        </div>
      }
      className="py-14"
    />
  );
}

/** Placeholder rows while the first page loads, so the table does not jump in. */
export function LoadingRows() {
  return (
    <div role="status" className="divide-y divide-border-subtle">
      <span className="sr-only">Loading sources</span>
      {[0.6, 0.45, 0.7, 0.5, 0.55].map((width, index) => (
        <div key={index} className="flex h-12 items-center gap-4 px-6">
          <Skeleton shape="tile" className="size-4" />
          <Skeleton className="h-3" style={{ width: `${width * 50}%` }} />
          <Skeleton className="ml-auto h-3 w-16" />
        </div>
      ))}
    </div>
  );
}

export type SourcesEmptyReason = 'linked-missing' | 'folder-missing' | 'library-empty' | 'no-match' | 'nothing-unfiled' | 'folder-empty';

export interface SourcesEmptyStateProps {
  reason: SourcesEmptyReason;
  /** The open folder, if one is open. */
  node: FolderNode | undefined;
  unfiled: boolean;
  /** The applied search. */
  query: string;
  onShowAll: () => void;
  onShowAllFolders: () => void;
  onClearFilters: () => void;
  onAdd: (kind: AddSourceKind) => void;
}

/** Why the list is empty, and the one thing to do about it. */
export function SourcesEmptyState({ reason, node, unfiled, query, onShowAll, onShowAllFolders, onClearFilters, onAdd }: SourcesEmptyStateProps) {
  switch (reason) {
    case 'linked-missing':
      return (
        <EmptyState
          icon={SearchX}
          title="Source not found"
          description="It may have been deleted."
          action={<Button onClick={onShowAll}>Show all sources</Button>}
        />
      );
    case 'folder-missing':
      return (
        <EmptyState
          icon={FolderX}
          title="Folder not found"
          description="It may have been deleted or moved."
          action={<Button onClick={onShowAllFolders}>Show all sources</Button>}
        />
      );
    case 'library-empty':
      return <EmptyLibrary onAdd={onAdd} />;
    case 'no-match': {
      const scope = node ? ` in “${node.folder.name}”` : unfiled ? ' among the unfiled sources' : '';
      return (
        <EmptyState
          icon={SearchX}
          title="No sources match your filters"
          description={
            query ? `Nothing matches “${query}” with the selected filters${scope}.` : `No sources have the selected type, status and site${scope}.`
          }
          action={
            <Button icon={FilterX} onClick={onClearFilters}>
              Clear filters
            </Button>
          }
        />
      );
    }
    case 'nothing-unfiled':
      return <EmptyState icon={Inbox} title="Nothing unfiled" description="Every source is in a folder." />;
    case 'folder-empty':
      return node && node.totalCount > node.folder.sourceCount ? (
        <EmptyState
          icon={FolderOpen}
          title="No sources directly in this folder"
          description={`Its subfolders hold ${pluralize(node.totalCount - node.folder.sourceCount, 'source')}. Open one, or move sources here with “Move to folder”.`}
        />
      ) : (
        <EmptyState
          icon={FolderOpen}
          title="This folder is empty"
          description="Add sources while the folder is open, or move existing ones here with “Move to folder” or by dragging them onto it."
          action={
            <Button icon={Plus} onClick={() => onAdd('url')}>
              Add source
            </Button>
          }
        />
      );
  }
}
