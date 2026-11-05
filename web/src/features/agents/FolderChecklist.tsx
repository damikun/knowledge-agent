import { clsx } from 'clsx';
import { Folder } from 'lucide-react';
import { Link } from 'react-router';
import { pluralize } from '../../lib/format';
import { flattenTree, type FolderNode } from '../sources/folders';
import { coveringFolder } from './agentForm';
import { useReturnState } from '../../lib/returnTo';

export interface FolderChecklistProps {
  tree: readonly FolderNode[];
  selected: readonly string[];
  onToggle: (node: FolderNode) => void;
  describedBy: string;
}

export function FolderChecklist({ tree, selected, onToggle, describedBy }: FolderChecklistProps) {
  const returnState = useReturnState();
  const nodes = flattenTree(tree);
  if (nodes.length === 0) {
    return (
      <p className="text-small text-fg-secondary">
        There are no folders yet. Create them on the{' '}
        <Link to="/sources" state={returnState} className="font-medium text-accent-text hover:underline">
          Sources
        </Link>{' '}
        page.
      </p>
    );
  }
  const chosen = new Set(selected);
  return (
    <div role="group" aria-label="Folders" aria-describedby={describedBy} className="max-h-72 overflow-y-auto rounded-lg border border-border p-1">
      {nodes.map((node) => {
        const covered = coveringFolder(tree, chosen, node.folder.id) !== undefined;
        const checked = covered || chosen.has(node.folder.id);
        return (
          <label
            key={node.folder.id}
            style={{ paddingLeft: `${0.5 + (node.depth - 1) * 1.25}rem` }}
            className={clsx(
              'flex min-h-8 items-center gap-2 rounded-md py-1 pr-2 text-sm',
              covered ? 'cursor-not-allowed text-fg-muted' : 'cursor-pointer text-fg',
              checked && !covered ? 'bg-accent-subtle' : !covered && 'hover:bg-fill-hover',
            )}
          >
            <input
              type="checkbox"
              checked={checked}
              disabled={covered}
              onChange={() => onToggle(node)}
              aria-describedby={`${describedBy}-count-${node.folder.id}`}
              className="size-4 shrink-0"
            />
            <Folder className="size-4 shrink-0 text-fg-muted" aria-hidden />
            <span className="min-w-0 flex-1 truncate">{node.folder.name}</span>
            {covered ? <span className="shrink-0 text-xs">Included</span> : null}
            {/* The count describes the checkbox rather than naming it, so the folder's name stays its name. */}
            <span aria-hidden className="shrink-0 text-xs text-fg-muted tabular-nums">
              {node.totalCount}
            </span>
            <span id={`${describedBy}-count-${node.folder.id}`} hidden>
              {pluralize(node.totalCount, 'source')}
            </span>
          </label>
        );
      })}
    </div>
  );
}
