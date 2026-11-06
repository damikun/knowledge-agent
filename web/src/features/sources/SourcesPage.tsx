import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Hourglass, Plus, Search, TextSearch } from 'lucide-react';
import { useMemo, useRef, useState, type ReactNode } from 'react';
import { useSearchParams, type To } from 'react-router';
import { Button } from '../../components/ui/Button';
import { Drawer } from '../../components/ui/Drawer';
import { ErrorMessage, PageHeader } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { SideSheet } from '../../components/ui/SideSheet';
import { toast } from '../../components/ui/Toast';
import { errorMessage } from '../../lib/api';
import { pluralize } from '../../lib/format';
import type { Guid } from '../../lib/types';
import { useDebouncedSearch } from '../../lib/hooks/useDebouncedSearch';
import { useOnChange } from '../../lib/hooks/useOnChange';
import { useProfiles } from '../models/api';
import { embeddingKey, findEmbeddingProfile, staleEmbeddings, staleEmbeddingSources } from '../models/embedding';
import { AddSourceSheet, type AddSourceKind } from './AddSourceSheet';
import { sourceKeys, sourcesApi, useSourceFolders, useSourceSites, useSourcesPage, useSourceStats } from './api';
import type { DragPayload, FolderDrop } from './dragAndDrop';
import { FolderDialogHost, type FolderDialogState, type SourcesToMove } from './FolderDialogs';
import {
  buildFolderTree,
  findNode,
  flattenTree,
  folderPathLabel,
  MAX_FOLDER_DEPTH,
  moveFolderBlocker,
  parseFolderView,
  UNFILED,
  viewFolderId,
  type FolderNode,
  type FolderView,
} from './folders';
import { FolderTree } from './FolderTree';
import { LibraryFolderBar } from './LibraryFolderBar';
import { anyIngesting, hasActiveIngestion, SOURCE_POLL_INTERVAL_MS } from './polling';
import { SearchPlayground } from './SearchPlayground';
import { SourceDetailsSheet } from './SourceDetailsSheet';
import { EMPTY_SELECTION, select, selectPage, type Selection } from './selection';
import { hasActiveFilters, lastPageOffset, statusFilterOptions, toListParams, type StatusFilter, type TypeFilter } from './sourceFilters';
import { LoadingRows, SourcesEmptyState } from './SourcesEmptyState';
import { StatusFilters, TypeAndSiteFilters } from './SourceFilterControls';
import { SourcesTable } from './SourcesTable';
import { SelectionBar, SourcesPager } from './SourcesTableBars';
import type { MoveSourcesResponse, SourceDto, SourceListParams, SourcePageDto } from './types';
import { WaitingSourcesMessage } from './WaitingSourcesMessage';
import { LibrarySummary, ReindexNotice } from './LibraryNotices';
import { readFoldersHidden, readPageSize, saveFoldersHidden, savePageSize } from './sourcesPreferences';
import { useKeptReturnState } from '../../lib/returnTo';

function withView(params: URLSearchParams, view: FolderView): URLSearchParams {
  const next = new URLSearchParams(params);
  next.delete('source');
  if (view === 'all') {
    next.delete('folder');
  } else {
    next.set('folder', view);
  }
  return next;
}

export function SourcesPage() {
  const queryClient = useQueryClient();
  const profiles = useProfiles();
  const [searchParams, setSearchParams] = useSearchParams();
  // Linked from a citation ("Show in Sources"): the library shows just that source.
  const linkedId = searchParams.get('source')?.trim() || null;
  // Opened from a citation, the way back to the answer survives "Show all".
  const kept = useKeptReturnState();
  const view = parseFolderView(searchParams.get('folder'));

  const folders = useSourceFolders();
  const tree = useMemo(() => buildFolderTree(folders.data?.folders ?? []), [folders.data]);
  const currentNode = findNode(tree, viewFolderId(view));
  const folderMissing = viewFolderId(view) !== null && folders.isSuccess && !currentNode;
  const folderLabel = (id: Guid) => folderPathLabel(tree, id);

  const search = useDebouncedSearch();
  const [type, setType] = useState<TypeFilter>('all');
  const [status, setStatus] = useState<StatusFilter>('all');
  const [site, setSite] = useState('');
  const [pageSize, setPageSize] = useState(readPageSize);
  const filters = { query: search.query, type, status, site };
  const filtered = hasActiveFilters(filters);

  // An offset belongs to the filters it was chosen for, so changing any filter starts at the first page, also when the
  // filters return to earlier values. The stored offset is reset during render.
  const filtersKey = JSON.stringify([search.query, type, status, site, linkedId, view, pageSize]);
  const [paging, setPaging] = useState({ filtersKey, offset: 0 });
  if (paging.filtersKey !== filtersKey) {
    setPaging({ filtersKey, offset: 0 });
  }
  const offset = paging.filtersKey === filtersKey ? paging.offset : 0;
  const goTo = (nextOffset: number) => setPaging({ filtersKey, offset: Math.max(0, nextOffset) });

  const stats = useSourceStats();
  const polling = hasActiveIngestion(stats.data);
  const params: SourceListParams = linkedId
    ? { ids: [linkedId], limit: pageSize }
    : { ...toListParams(filters, offset, pageSize), folder: view === 'all' ? null : view };
  // The page also polls while one of its own sources is ingesting: the stats may already be idle when the page was read
  // just before ingestion finished.
  const page = useSourcesPage(params, { refetchInterval: (data) => (polling || anyIngesting(data?.items) ? SOURCE_POLL_INTERVAL_MS : false) });
  const sites = useSourceSites(params, !linkedId);
  const pageData = page.data;
  const items = useMemo(() => pageData?.items ?? [], [pageData]);
  const total = pageData?.total ?? 0;

  // A selection belongs to the page it was made on, like the offset belongs to its filters.
  const pageKey = `${filtersKey}:${offset}`;
  const [selectionState, setSelectionState] = useState<{ pageKey: string; selection: Selection }>({ pageKey, selection: EMPTY_SELECTION });
  const selection = selectionState.pageKey === pageKey ? selectionState.selection : EMPTY_SELECTION;
  const setSelection = (next: Selection) => setSelectionState({ pageKey, selection: next });
  const clearSelection = () => setSelection(EMPTY_SELECTION);
  const pageIds = useMemo(() => items.map((source) => source.id), [items]);
  const selectedSources = useMemo(() => (selection.allMatching ? items : items.filter((source) => selection.ids.has(source.id))), [items, selection]);
  const visibleSelection = useMemo(() => new Set(selectedSources.map((source) => source.id)), [selectedSources]);
  const selectedCount = selection.allMatching ? total : selectedSources.length;
  const toMove: SourcesToMove = selection.allMatching ? { kind: 'matching', params, count: total } : { kind: 'sources', sources: selectedSources };

  const [details, setDetails] = useState<SourceDto | null>(null);
  const [testing, setTesting] = useState(false);
  const searchQueryRef = useRef<HTMLInputElement>(null);
  const [foldersHidden, setFoldersHidden] = useState(readFoldersHidden);
  const toggleFolders = () => {
    setFoldersHidden(!foldersHidden);
    saveFoldersHidden(!foldersHidden);
  };
  const [pendingDelete, setPendingDelete] = useState<SourceDto | null>(null);
  const [folderDialog, setFolderDialog] = useState<FolderDialogState | null>(null);
  const [foldersOpen, setFoldersOpen] = useState(false);
  const [adding, setAdding] = useState<{ open: boolean; kind: AddSourceKind }>({ open: false, kind: 'url' });
  const openAdd = (kind: AddSourceKind) => setAdding({ open: true, kind });
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(new Set());
  const [refreshing, setRefreshing] = useState(false);

  const embeddingProfile = findEmbeddingProfile(profiles.data);
  const stale = staleEmbeddings(stats.data?.readyByEmbeddingModel, embeddingProfile);
  const staleIds = useMemo(() => new Set(staleEmbeddingSources(items, embeddingProfile).map((s) => s.id)), [items, embeddingProfile]);

  // A page past the end (its last items were deleted or finished elsewhere) moves to the last page, during render like
  // the reset of the offset above.
  const settledPage = page.isPlaceholderData ? undefined : pageData;
  if (settledPage && settledPage.items.length === 0 && settledPage.total > 0 && offset > 0) {
    const last = lastPageOffset(settledPage.total, pageSize);
    if (last !== offset) {
      setPaging({ filtersKey, offset: last });
    }
  }

  // When ingestion finishes, refresh the page once more: its last poll may predate the final status.
  useOnChange(polling, (now) => {
    if (!now) {
      void queryClient.invalidateQueries({ queryKey: sourceKeys.pages });
    }
  });

  const hrefFor = (next: FolderView): To => {
    const query = withView(searchParams, next).toString();
    return { search: query ? `?${query}` : '' };
  };
  const openView = (next: FolderView) => setSearchParams((current) => withView(current, next));

  const showAll = () =>
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.delete('source');
        return next;
      },
      { replace: true, state: kept },
    );

  const clearFilters = () => {
    search.clear();
    setType('all');
    setStatus('all');
    setSite('');
  };

  const refresh = () => {
    setRefreshing(true);
    void queryClient.invalidateQueries({ queryKey: sourceKeys.all }).finally(() => setRefreshing(false));
  };

  const reportMove = (moved: number, folderId: Guid | null) => {
    clearSelection();
    const text = folderId
      ? `Moved ${pluralize(moved, 'source')} to “${folderLabel(folderId) || 'the folder'}”.`
      : moved === 1
        ? 'Moved 1 source out of its folder.'
        : `Moved ${pluralize(moved, 'source')} out of their folders.`;
    toast({ title: text, tone: 'success' });
  };

  const reingest = useMutation({
    mutationFn: (id: string) => sourcesApi.reingestSource(id),
    onMutate: (id) => setBusyIds((current) => new Set(current).add(id)),
    onError: (error) => toast({ title: 'Re-ingest failed', description: errorMessage(error), tone: 'danger' }),
    onSettled: (_data, _error, id) => {
      setBusyIds((current) => {
        const next = new Set(current);
        next.delete(id);
        return next;
      });
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
  });

  const remove = useMutation({
    mutationFn: (source: SourceDto) => sourcesApi.deleteSource(source.id),
    onSuccess: (_data, source) => {
      setPendingDelete(null);
      if (source.id === linkedId) {
        showAll();
      } else if (offset > 0 && items.length === 1 && items[0]?.id === source.id) {
        // Deleting the last item of a page shows the previous page instead of an empty one.
        goTo(offset - pageSize);
      } else {
        queryClient.setQueryData<SourcePageDto>(sourceKeys.page(params), (old) =>
          old ? { ...old, items: old.items.filter((s) => s.id !== source.id), total: Math.max(0, old.total - 1) } : old,
        );
      }
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
  });

  const dropMove = useMutation({
    mutationFn: ({ payload, folderId }: { payload: DragPayload; folderId: Guid | null }): Promise<MoveSourcesResponse | undefined> => {
      switch (payload.kind) {
        case 'folder':
          return sourcesApi.moveFolder(payload.id, folderId).then(() => undefined);
        case 'matching':
          return sourcesApi.moveMatchingSources(payload.params, folderId);
        default:
          return sourcesApi.moveSources(payload.ids, folderId);
      }
    },
    onSuccess: (result, { payload, folderId }) => {
      if (payload.kind === 'folder') {
        const name = findNode(tree, payload.id)?.folder.name ?? 'The folder';
        toast({ title: folderId ? `Moved “${name}” into “${folderLabel(folderId)}”.` : `Moved “${name}” to the top level.`, tone: 'success' });
      } else {
        reportMove(result?.moved ?? (payload.kind === 'sources' ? payload.ids.length : payload.count), folderId);
      }
      void queryClient.invalidateQueries({ queryKey: sourceKeys.all });
    },
    onError: (error) => toast({ title: 'Could not move', description: errorMessage(error), tone: 'danger' }),
  });

  // Drops that would change nothing, or nest a folder into itself or too deep, are refused while dragging.
  const folderDrop: FolderDrop = {
    canDrop: (payload, folderId) => {
      if (payload.kind === 'folder') {
        const node = findNode(tree, payload.id);
        const target = folderId ? findNode(tree, folderId) : null;
        return node !== undefined && target !== undefined && moveFolderBlocker(node, target) === null;
      }
      if (payload.kind === 'matching') {
        return (payload.params.folder ?? null) !== (folderId ?? UNFILED);
      }
      return payload.ids.some((id) => (items.find((source) => source.id === id)?.folderId ?? null) !== folderId);
    },
    drop: (payload, folderId) => dropMove.mutate({ payload, folderId }),
  };

  const dragRows = (source: SourceDto): { payload: DragPayload; label: string } => {
    if (!visibleSelection.has(source.id)) {
      // Like a file manager, dragging an unselected row selects it and drags only that row.
      setSelection(select(selection, pageIds, source.id, 'only'));
      return { payload: { kind: 'sources', ids: [source.id] }, label: source.title };
    }
    if (selection.allMatching) {
      return { payload: { kind: 'matching', params, count: total }, label: pluralize(total, 'source') };
    }
    return selectedSources.length === 1
      ? { payload: { kind: 'sources', ids: [source.id] }, label: source.title }
      : { payload: { kind: 'sources', ids: selectedSources.map((item) => item.id) }, label: pluralize(selectedSources.length, 'source') };
  };

  const folderDeleted = (node: FolderNode) => {
    setFolderDialog(null);
    const wasOpen = currentNode !== undefined && flattenTree([node]).some((deleted) => deleted.folder.id === currentNode.folder.id);
    toast({ title: `Deleted “${node.folder.name}”. Its sources are in Unfiled now.`, tone: 'success' });
    if (wasOpen) {
      openView(node.folder.parentId ?? 'all');
    }
  };

  const libraryEmpty = stats.data?.total === 0;
  let content: ReactNode;
  if (page.isPending) {
    content = <LoadingRows />;
  } else if (page.isError) {
    content = (
      <div className="p-4">
        <ErrorMessage error={page.error} title="Could not load sources" />
      </div>
    );
  } else if (items.length === 0) {
    content = (
      <SourcesEmptyState
        reason={
          linkedId
            ? 'linked-missing'
            : folderMissing
              ? 'folder-missing'
              : libraryEmpty || (!filtered && view === 'all')
                ? 'library-empty'
                : filtered
                  ? 'no-match'
                  : view === UNFILED
                    ? 'nothing-unfiled'
                    : 'folder-empty'
        }
        node={currentNode}
        unfiled={view === UNFILED}
        query={search.query}
        onShowAll={showAll}
        onShowAllFolders={() => openView('all')}
        onClearFilters={clearFilters}
        onAdd={openAdd}
      />
    );
  } else {
    content = (
      <div
        aria-busy={page.isPlaceholderData || undefined}
        className={clsx('flex min-h-0 flex-1 flex-col transition-opacity', page.isPlaceholderData && 'opacity-60')}
      >
        <SourcesTable
          key={pageKey}
          sources={items}
          highlightId={linkedId}
          staleIds={staleIds}
          busyIds={busyIds}
          selectedIds={visibleSelection}
          onSelect={(source, mode) => setSelection(select(selection, pageIds, source.id, mode))}
          onSelectPage={(selected) => setSelection(selectPage(pageIds, selected))}
          onClearSelection={clearSelection}
          onDragRows={dragRows}
          onSite={setSite}
          // Only the All sources view mixes sources from different folders.
          folderLabel={(source) => (view === 'all' && source.folderId ? folderLabel(source.folderId) || null : null)}
          onOpen={setDetails}
          onMove={(source) => setFolderDialog({ kind: 'move-sources', sources: { kind: 'sources', sources: [source] } })}
          onReingest={(source) => reingest.mutate(source.id)}
          onDelete={(source) => {
            remove.reset();
            setPendingDelete(source);
          }}
        />
      </div>
    );
  }

  // An empty library has nothing to filter, unless filters emptied it.
  const showFilters = !libraryEmpty || filtered;
  const statusOptions = statusFilterOptions(stats.data, view === 'all' && !search.query && type === 'all' && !site);
  const selectionText = selection.allMatching ? `All ${pluralize(total, 'source')} selected` : `${pluralize(selectedCount, 'source')} selected`;
  const treeProps = {
    tree,
    view,
    totalCount: folders.data?.totalCount,
    unfiledCount: folders.data?.unfiledCount,
    loading: folders.isPending,
    error: folders.error,
    hrefFor,
    drop: folderDrop,
    onNewFolder: () =>
      setFolderDialog({ kind: 'create', parentId: currentNode && currentNode.depth < MAX_FOLDER_DEPTH ? currentNode.folder.id : null }),
  };
  const filterControls = (
    <StatusFilters options={statusOptions} value={status} onChange={setStatus} onClear={filtered || search.input.trim() ? clearFilters : undefined} />
  );
  const filterSelects = <TypeAndSiteFilters type={type} onType={setType} site={site} onSite={setSite} sites={sites.data ?? []} />;
  // The open source follows the polled page, so its status and counts stay current.
  const detailsSource = details ? (items.find((source) => source.id === details.id) ?? details) : null;

  return (
    <div className="relative h-full overflow-y-auto">
      {/* Like <Page>: one scrolling column with the shared gutter; the table card is as tall as its rows. */}
      <div className="flex flex-col gap-5 px-4 py-6 lg:px-6">
        <PageHeader
          title="Sources"
          description="Everything your agents can search: web pages, files and text."
          actions={
            <>
              <Button icon={TextSearch} onClick={() => setTesting(true)}>
                Test search
              </Button>
              <Button variant="primary" icon={Plus} onClick={() => openAdd('url')}>
                Add source
              </Button>
            </>
          }
        />
        {/* For screen readers only, so it takes no row (and no gap) in the layout. */}
        {stats.data && stats.data.total > 0 ? (
          <div aria-live="polite" className="sr-only">
            <LibrarySummary stats={stats.data} />
          </div>
        ) : null}

        {stale.count > 0 && embeddingProfile?.effective ? (
          <ReindexNotice count={stale.count} models={stale.models} currentKey={embeddingKey(embeddingProfile.effective)} />
        ) : null}

        <div className="flex items-start gap-4">
          {/* Beside the list, so sources can be dragged onto any folder; it hides to give the list the room. */}
          {/* Not an aside: the tree is a "Folders" navigation landmark of its own. */}
          <div id="source-folders" className={clsx('sticky top-0 w-48 shrink-0', foldersHidden ? 'hidden' : 'hidden lg:block')}>
            <FolderTree {...treeProps} />
          </div>

          <section className="flex min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-card">
            {/* A compact notice in the table instead of a page banner: what waits, and where to fix it. */}
            {stats.data && stats.data.waiting > 0 ? (
              <div
                role="status"
                className="flex items-start gap-2 border-b border-border-subtle bg-warning-subtle px-6 py-2 text-small text-warning-text"
              >
                <Hourglass className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden />
                <span>
                  <WaitingSourcesMessage count={stats.data.waiting} />
                </span>
              </div>
            ) : null}
            {linkedId ? (
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle bg-surface-subtle px-6 py-2 text-small text-fg-secondary">
                <span>Showing the source linked from a citation.</span>
                <Button size="sm" variant="ghost" onClick={showAll}>
                  Show all sources
                </Button>
              </div>
            ) : (
              <>
                <div className="border-b border-border-subtle px-6 py-2">
                  <LibraryFolderBar
                    view={view}
                    node={currentNode}
                    loading={folders.isPending}
                    tree={tree}
                    hrefFor={hrefFor}
                    drop={folderDrop}
                    onShowFolders={() => setFoldersOpen(true)}
                    foldersHidden={foldersHidden}
                    onToggleFolders={toggleFolders}
                    // The search shares the folder's row, so the chips, type and site fit one row beside the folders.
                    end={
                      showFilters ? (
                        <>
                          {view === 'all' ? <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">{filterControls}</div> : null}
                          <form
                            role="search"
                            aria-label="Search sources"
                            className="relative w-full sm:w-60"
                            onSubmit={(event) => {
                              event.preventDefault();
                              search.flush();
                            }}
                          >
                            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
                            <input
                              type="search"
                              aria-label="Search sources"
                              value={search.input}
                              onChange={(event) => search.setInput(event.target.value)}
                              placeholder="Search title, URL or file"
                              // 28 px like the folder buttons in this row.
                              className="field h-7 py-0 pl-8 text-small"
                            />
                          </form>
                          {view === 'all' ? filterSelects : null}
                        </>
                      ) : null
                    }
                    onNewSubfolder={(node) => setFolderDialog({ kind: 'create', parentId: node.folder.id })}
                    onRename={(node) => setFolderDialog({ kind: 'rename', node })}
                    onMove={(node) => setFolderDialog({ kind: 'move', node })}
                    onDelete={(node) => setFolderDialog({ kind: 'delete', node })}
                  />
                </div>

                {showFilters && view !== 'all' ? (
                  <div className="flex flex-wrap items-center gap-2 border-b border-border-subtle px-6 py-2.5">
                    <div className="mr-auto flex flex-wrap items-center gap-2">{filterControls}</div>
                    {filterSelects}
                  </div>
                ) : null}
              </>
            )}

            <SelectionBar
              text={selectedCount > 0 ? selectionText : null}
              onSelectAll={
                !selection.allMatching && selectedSources.length === items.length && total > items.length
                  ? { total, select: () => setSelection({ ids: new Set(), anchor: null, allMatching: true }) }
                  : undefined
              }
              onMove={() => setFolderDialog({ kind: 'move-sources', sources: toMove })}
              onClear={clearSelection}
            />

            <div className="flex flex-col">{content}</div>

            {pageData && (items.length > 0 || polling) ? (
              <SourcesPager
                page={pageData}
                placeholder={page.isPlaceholderData}
                pageSize={pageSize}
                onPageSize={(size) => {
                  setPageSize(size);
                  savePageSize(size);
                }}
                polling={polling}
                refreshing={refreshing}
                onRefresh={refresh}
                onShowPage={goTo}
              />
            ) : null}
          </section>
        </div>
      </div>

      <Drawer open={foldersOpen} onClose={() => setFoldersOpen(false)} label="Folders">
        <div className="overflow-y-auto p-3">
          <FolderTree
            {...treeProps}
            onNavigate={() => setFoldersOpen(false)}
            onNewFolder={() => {
              setFoldersOpen(false);
              treeProps.onNewFolder();
            }}
          />
        </div>
      </Drawer>

      <SideSheet
        open={testing}
        onClose={() => setTesting(false)}
        size="lg"
        initialFocus={searchQueryRef}
        title="Test search"
        description="Ask what a visitor would ask and see which passages the agent finds: vector and keyword results fused by rank, then reranked when a reranker is set up."
      >
        <SearchPlayground queryRef={searchQueryRef} />
      </SideSheet>
      <SourceDetailsSheet
        source={detailsSource}
        folder={detailsSource?.folderId ? folderLabel(detailsSource.folderId) || null : null}
        stale={detailsSource ? staleIds.has(detailsSource.id) : false}
        onClose={() => setDetails(null)}
      />

      <AddSourceSheet
        open={adding.open}
        kind={adding.kind}
        onKindChange={(kind) => setAdding({ open: true, kind })}
        onClose={() => setAdding((current) => ({ ...current, open: false }))}
        folder={currentNode ? { id: currentNode.folder.id, label: folderLabel(currentNode.folder.id) } : null}
      />
      <ConfirmDialog
        open={pendingDelete !== null}
        title="Delete source?"
        message={
          <>
            “{pendingDelete?.title}” will be removed together with{' '}
            {pendingDelete?.type === 'website'
              ? `its ${pendingDelete.pageCount ?? 0} pages and their vectors`
              : `its ${pendingDelete?.chunkCount ?? 0} chunks and vectors`}
            . Answers that cited it keep their text.
          </>
        }
        confirmLabel="Delete source"
        busy={remove.isPending}
        error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
        onConfirm={() => pendingDelete && remove.mutate(pendingDelete)}
        onCancel={() => setPendingDelete(null)}
      />

      {folderDialog ? (
        <FolderDialogHost
          dialog={folderDialog}
          tree={tree}
          onClose={() => setFolderDialog(null)}
          onCreated={(folder) => {
            setFolderDialog(null);
            openView(folder.id);
          }}
          onMoved={(moved, folderId) => {
            setFolderDialog(null);
            reportMove(moved, folderId);
          }}
          onDeleted={folderDeleted}
        />
      ) : null}
    </div>
  );
}
