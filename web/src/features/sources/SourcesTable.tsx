import { clsx } from 'clsx';
import { ExternalLink, FileText, Folder, FolderInput, PanelRight, RefreshCw, Trash, TriangleAlert } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { Button } from '../../components/ui/Button';
import { Menu } from '../../components/ui/Menu';
import { Table, TableCell, TableHead, TableHeader, TableRow, TableRowHeader } from '../../components/ui/Table';
import { formatBytes, formatDateTime, formatNumber, formatRelativeTime, hostname, pluralize, safeExternalUrl } from '../../lib/format';
import { endDrag, startDrag, type DragPayload } from './dragAndDrop';
import type { SelectMode } from './selection';
import { SourceStatusBadge } from './SourceStatusBadge';
import { SOURCE_TYPES } from './sourceTypes';
import type { SourceDto } from './types';

// The chunk count gives way first when the list is narrow; size, pages, tokens and the model are in the details.
const CHUNKS_COLUMN = 'hidden @min-[36rem]:table-cell';

function SourceTypeIcon({ type }: { type: string }) {
  const meta = SOURCE_TYPES[type?.toLowerCase()] ?? { icon: FileText, label: type };
  const Icon = meta.icon;
  return (
    <span title={meta.label} className="flex size-7 shrink-0 items-center justify-center rounded-md bg-fill-muted text-fg-muted">
      <Icon className="size-4" aria-hidden />
      <span className="sr-only">{meta.label}</span>
    </span>
  );
}

export interface SourcesTableProps {
  sources: SourceDto[];
  highlightId?: string | null;
  staleIds: ReadonlySet<string>;
  busyIds: ReadonlySet<string>;
  selectedIds: ReadonlySet<string>;
  onSelect: (source: SourceDto, mode: SelectMode) => void;
  onSelectPage: (selected: boolean) => void;
  onClearSelection: () => void;
  onDragRows: (source: SourceDto) => { payload: DragPayload; label: string };
  onSite: (site: string) => void;
  folderLabel: (source: SourceDto) => string | null;
  onOpen: (source: SourceDto) => void;
  onMove: (source: SourceDto) => void;
  onReingest: (source: SourceDto) => void;
  onDelete: (source: SourceDto) => void;
}

function Subtitle({ source, folder, onSite }: { source: SourceDto; folder: string | null; onSite: (site: string) => void }) {
  const site = source.site ?? (source.url ? hostname(source.url) : null);
  const details = [
    source.url ? null : source.fileName,
    source.sizeBytes != null ? formatBytes(source.sizeBytes) : null,
    source.type === 'website' && source.pageCount != null ? pluralize(source.pageCount, 'page') : null,
  ].filter(Boolean);
  if (!site && details.length === 0 && !folder) {
    return null;
  }
  return (
    <div className="flex min-w-0 items-center gap-1.5 text-xs font-normal text-fg-muted">
      {folder ? (
        <span className="inline-flex max-w-[45%] shrink-0 items-center gap-1" title={folder}>
          <Folder className="size-3 shrink-0" aria-hidden />
          <span className="sr-only">Folder:</span>
          <span className="truncate">{folder}</span>
        </span>
      ) : null}
      {folder && (site || details.length > 0) ? <span aria-hidden>·</span> : null}
      {source.site ? (
        <button
          type="button"
          onClick={() => onSite(source.site!)}
          aria-label={`Show only pages from ${source.site}`}
          title={`Show only pages from ${source.site}`}
          className="min-w-0 truncate rounded-sm underline-offset-2 hover:text-fg hover:underline"
        >
          {source.site}
        </button>
      ) : site ? (
        <span className="truncate">{site}</span>
      ) : null}
      {site && details.length > 0 ? <span aria-hidden>·</span> : null}
      {details.length > 0 ? <span className="truncate">{details.join(' · ')}</span> : null}
    </div>
  );
}

function SelectPage({ sources, selectedIds, onSelectPage }: Pick<SourcesTableProps, 'sources' | 'selectedIds' | 'onSelectPage'>) {
  const selected = sources.filter((source) => selectedIds.has(source.id)).length;
  const all = selected > 0 && selected === sources.length;
  const some = selected > 0 && !all;
  return (
    <input
      // Mixed has no attribute; the DOM property is set on every render that changes it.
      ref={(input) => {
        if (input) {
          input.indeterminate = some;
        }
      }}
      type="checkbox"
      aria-label="Select every source on this page"
      checked={all}
      onChange={(event) => onSelectPage(event.target.checked)}
      className="block size-4"
    />
  );
}

export function SourcesTable({
  sources,
  highlightId,
  staleIds,
  busyIds,
  selectedIds,
  onSelect,
  onSelectPage,
  onClearSelection,
  onDragRows,
  onSite,
  folderLabel,
  onOpen,
  onMove,
  onReingest,
  onDelete,
}: SourcesTableProps) {
  const highlightRef = useRef<HTMLTableRowElement>(null);
  const tableRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (highlightId) {
      highlightRef.current?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }, [highlightId, sources.length]);

  return (
    <div
      ref={tableRef}
      tabIndex={-1}
      // Fills the space it is given and scrolls inside it, so the header row stays in view over a long page.
      className="@container flex min-h-0 flex-1 flex-col"
      onKeyDown={(event) => {
        if (event.key === 'Escape' && selectedIds.size > 0) {
          onClearSelection();
        }
      }}
    >
      <Table aria-label="Sources" containerClassName="min-h-0 flex-1 overflow-auto" className="min-w-[30rem]">
        <TableHead>
          <TableHeader className="w-10">
            <SelectPage sources={sources} selectedIds={selectedIds} onSelectPage={onSelectPage} />
          </TableHeader>
          <TableHeader>Title</TableHeader>
          <TableHeader>Status</TableHeader>
          <TableHeader numeric className={CHUNKS_COLUMN}>
            Chunks
          </TableHeader>
          <TableHeader>Added</TableHeader>
          <TableHeader>
            <span className="sr-only">Actions</span>
          </TableHeader>
        </TableHead>
        <tbody>
          {sources.map((source) => {
            const url = safeExternalUrl(source.url);
            const highlighted = source.id === highlightId;
            const stale = staleIds.has(source.id);
            const busy = busyIds.has(source.id);
            const selected = selectedIds.has(source.id);
            const failed = source.status?.toLowerCase() === 'failed';
            return (
              <TableRow
                key={source.id}
                ref={highlighted ? highlightRef : undefined}
                // A source linked from a citation gets the selected look without joining the selection.
                selected={selected || highlighted}
                data-selected={selected || undefined}
                data-highlighted={highlighted || undefined}
                draggable
                onDragStart={(event) => {
                  const { payload, label } = onDragRows(source);
                  startDrag(event, payload, label);
                }}
                onDragEnd={endDrag}
                // Shift-click selects rows, not text.
                onMouseDown={(event) => event.shiftKey && event.preventDefault()}
                onClick={(event) => {
                  if ((event.target as HTMLElement).closest('a, button, input, select, textarea, label')) {
                    return;
                  }
                  // Focus follows the click as in a file manager, so Escape also clears a selection made with the mouse.
                  tableRef.current?.focus({ preventScroll: true });
                  onSelect(source, event.shiftKey ? 'range' : event.metaKey || event.ctrlKey ? 'toggle' : 'only');
                }}
                className="cursor-default"
              >
                <TableCell>
                  <input
                    type="checkbox"
                    aria-label={`Select “${source.title}”`}
                    checked={selected}
                    readOnly
                    onClick={(event) => onSelect(source, event.shiftKey ? 'add-range' : 'toggle')}
                    className="block size-4"
                  />
                </TableCell>
                {/* Full width with max-width 0 makes the title take the space left over and truncate instead of widening the table. */}
                <TableRowHeader className="w-full max-w-0">
                  <div className="flex min-w-0 items-center gap-3">
                    <SourceTypeIcon type={source.type} />
                    <div className="min-w-0 flex-1">
                      <div className="flex min-w-0 items-center gap-1">
                        <button
                          type="button"
                          data-source-title
                          onClick={() => onOpen(source)}
                          title={source.type === 'website' ? 'Show details and pages' : 'Show details and chunks'}
                          className="min-w-0 truncate rounded-sm text-left underline-offset-2 hover:underline"
                        >
                          {source.title || source.fileName || url || 'Untitled'}
                        </button>
                        {url ? (
                          <a
                            href={url}
                            target="_blank"
                            rel="noopener noreferrer"
                            aria-label={`Open “${source.title || url}” in a new tab`}
                            title={url}
                            className="flex size-5 shrink-0 items-center justify-center rounded-sm text-fg-muted hover:text-fg"
                          >
                            <ExternalLink className="size-3" aria-hidden />
                          </a>
                        ) : null}
                      </div>
                      <Subtitle source={source} folder={folderLabel(source)} onSite={onSite} />
                    </div>
                  </div>
                </TableRowHeader>
                <TableCell>
                  <span className="inline-flex items-center gap-1.5">
                    <SourceStatusBadge source={source} />
                    {stale ? (
                      <TriangleAlert
                        className="size-3.5 shrink-0 text-warning"
                        role="img"
                        aria-label="Embedded with another model than the current one"
                      />
                    ) : null}
                  </span>
                  {failed && source.error ? (
                    <p className="mt-1 line-clamp-2 max-w-64 text-xs break-words text-danger-text" title={source.error}>
                      {source.error}
                    </p>
                  ) : null}
                </TableCell>
                <TableCell numeric className={CHUNKS_COLUMN}>
                  {source.type === 'website' ? '–' : formatNumber(source.chunkCount)}
                </TableCell>
                <TableCell className="whitespace-nowrap" title={formatDateTime(source.createdAt)}>
                  {formatRelativeTime(source.createdAt)}
                </TableCell>
                <TableCell className="py-1">
                  {/* Shown on hover or focus, while its menu is open, and always where nothing can hover. */}
                  <div
                    className={clsx(
                      'flex justify-end gap-0.5',
                      !busy &&
                        'opacity-0 group-hover/row:opacity-100 group-focus-within/row:opacity-100 group-data-selected/row:opacity-100 has-aria-expanded:opacity-100 [@media(hover:none)]:opacity-100',
                    )}
                  >
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      icon={PanelRight}
                      aria-label={`Details of “${source.title}”`}
                      title="Details and chunks"
                      onClick={() => onOpen(source)}
                    />
                    <Menu
                      label={`Actions for “${source.title}”`}
                      busy={busy}
                      items={[
                        { label: 'Move to folder', icon: FolderInput, onSelect: () => onMove(source) },
                        { label: 'Re-ingest', icon: RefreshCw, onSelect: () => onReingest(source) },
                        { label: 'Delete', icon: Trash, danger: true, onSelect: () => onDelete(source) },
                      ]}
                    />
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </tbody>
      </Table>
    </div>
  );
}
