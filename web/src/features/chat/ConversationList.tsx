import { useQueryClient } from '@tanstack/react-query';
import { MessageSquare, Search, SquarePen, X } from 'lucide-react';
import { useCallback, useMemo, useState, type KeyboardEvent, type RefObject } from 'react';
import { Button } from '../../components/ui/Button';
import { EmptyState, ErrorMessage } from '../../components/ui/feedback';
import { Kbd } from '../../components/ui/Field';
import { Skeleton } from '../../components/ui/Skeleton';
import type { DebouncedSearch } from '../../lib/hooks/useDebouncedSearch';
import { useLoadMoreWhenVisible } from '../../lib/hooks/useLoadMoreWhenVisible';
import { findListItem, useConversationList, useLabels } from './api';
import { groupConversations, type ConversationGroup } from './conversationGroups';
import { ConversationRow } from './ConversationRow';
import { LabelPicker } from './LabelPicker';
import { ActiveFilterChip, LabelFilter, NO_FILTER, type ListFilter } from './LabelFilter';
import { modKey, NEW_CHAT_SHORTCUT, SEARCH_SHORTCUT } from './shortcuts';
import type { ConversationListItem } from './types';
import type { ConversationActions } from './useConversationActions';

export interface ConversationListProps {
  activeId: string | null;
  basePath: string;
  onNewChat: () => void;
  onNavigate?: () => void;
  search: DebouncedSearch;
  filter: ListFilter;
  onFilterChange: (filter: ListFilter) => void;
  actions: ConversationActions;
  searchRef: RefObject<HTMLInputElement | null>;
}

const SKELETON_WIDTHS = ['72%', '58%', '80%', '64%', '50%', '76%'];

function SkeletonRows({ count }: { count: number }) {
  return (
    <ul aria-hidden className="space-y-0.5">
      {SKELETON_WIDTHS.slice(0, count).map((width, index) => (
        <li key={index} className="flex h-8 items-center px-2.5">
          <Skeleton className="h-3" style={{ width }} />
        </li>
      ))}
    </ul>
  );
}

function rowLinks(container: HTMLElement | null): HTMLElement[] {
  return container ? [...container.querySelectorAll<HTMLElement>('a[data-row-id]')] : [];
}

export function ConversationList({ activeId, basePath, onNewChat, onNavigate, search, filter, onFilterChange, actions, searchRef }: ConversationListProps) {
  const queryClient = useQueryClient();
  const filters = useMemo(() => ({ q: search.query, labelId: filter.labelId, pinned: filter.pinned }), [search.query, filter.labelId, filter.pinned]);
  const list = useConversationList(filters);
  const labels = useLabels();
  const labelList = labels.data?.labels ?? [];
  const labelsAvailable = labels.data?.available ?? false;
  // The scrolling list, in state so the next page's observer can watch inside it once it mounts.
  const [scroller, setScroller] = useState<HTMLDivElement | null>(null);
  const [focusId, setFocusId] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [labelTarget, setLabelTarget] = useState<{ id: string; anchor: HTMLElement } | null>(null);

  const filtered = filters.q !== '' || filter.labelId !== '' || filter.pinned;
  const items = useMemo(
    () => (list.data?.pages ?? []).flatMap((page) => page.items).filter((item) => !actions.hidden.has(item.id)),
    [list.data, actions.hidden],
  );

  // The open conversation stays reachable while a search or filter hides it.
  const openItem = filtered && activeId && !actions.hidden.has(activeId) && !items.some((item) => item.id === activeId) ? findListItem(queryClient, activeId) : undefined;
  const groups = useMemo<ConversationGroup[]>(() => {
    const grouped = groupConversations(items);
    return openItem ? [{ key: 'open', label: 'Open conversation', items: [openItem] }, ...grouped] : grouped;
  }, [items, openItem]);
  const ordered = useMemo(() => groups.flatMap((group) => group.items), [groups]);

  const tabbableId = ordered.some((item) => item.id === focusId) ? focusId : ordered.some((item) => item.id === activeId) ? activeId : (ordered[0]?.id ?? null);

  const { hasNextPage, isFetchingNextPage, fetchNextPage } = list;
  const sentinelRef = useLoadMoreWhenVisible(list, { root: scroller, rootMargin: '400px 0px' });

  const focusRow = useCallback(
    (id: string | undefined) => {
      if (!id) {
        return;
      }
      setFocusId(id);
      rowLinks(scroller)
        .find((link) => link.dataset.rowId === id)
        ?.focus();
    },
    [scroller],
  );

  const onListKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement;
    const id = target.dataset.rowId;
    if (!id) {
      return;
    }
    const index = ordered.findIndex((item) => item.id === id);
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        focusRow(ordered[Math.min(index + 1, ordered.length - 1)]?.id);
        if (index >= ordered.length - 3 && hasNextPage && !isFetchingNextPage) {
          void fetchNextPage();
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (index === 0) {
          searchRef.current?.focus();
        } else {
          focusRow(ordered[index - 1]?.id);
        }
        break;
      case 'Home':
        event.preventDefault();
        focusRow(ordered[0]?.id);
        break;
      case 'End':
        event.preventDefault();
        focusRow(ordered[ordered.length - 1]?.id);
        break;
      case 'F2':
        event.preventDefault();
        setEditingId(id);
        break;
      case 'ContextMenu':
        event.preventDefault();
        target.parentElement?.querySelector<HTMLButtonElement>('button[aria-haspopup]')?.click();
        break;
      default:
        if (event.key === 'F10' && event.shiftKey) {
          event.preventDefault();
          target.parentElement?.querySelector<HTMLButtonElement>('button[aria-haspopup]')?.click();
        }
    }
  };

  const onSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      if (search.input) {
        event.preventDefault();
        event.stopPropagation();
        search.clear();
      } else {
        event.currentTarget.blur();
      }
    } else if (event.key === 'Enter') {
      search.flush();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      focusRow(ordered[0]?.id);
    }
  };

  const clearAll = () => {
    search.clear();
    onFilterChange(NO_FILTER);
    searchRef.current?.focus();
  };

  const labelItem = labelTarget ? (ordered.find((item) => item.id === labelTarget.id) ?? findListItem(queryClient, labelTarget.id)) : undefined;

  const endRename = useCallback(
    (id: string) => {
      setEditingId(null);
      // Back on the row once it shows again, so keyboard users keep their place.
      window.setTimeout(() => focusRow(id), 0);
    },
    [focusRow],
  );
  const onEditLabels = useCallback((item: ConversationListItem, anchor: HTMLElement) => setLabelTarget({ id: item.id, anchor }), []);

  let body;
  if (list.isPending) {
    body = (
      <div role="status" aria-label="Loading conversations" className="px-0.5 pt-1">
        <SkeletonRows count={6} />
      </div>
    );
  } else if (list.isError && items.length === 0) {
    body = <ErrorMessage error={list.error} className="mx-1 text-xs" />;
  } else if (ordered.length === 0) {
    body = filtered ? (
      <EmptyState
        icon={Search}
        title="No matching conversations"
        description={filters.q ? `Nothing matches “${filters.q}”.` : 'No conversation has this label yet.'}
        action={
          <Button size="sm" onClick={clearAll}>
            {filters.q ? 'Clear search' : 'Clear filter'}
          </Button>
        }
        className="py-8"
      />
    ) : (
      <EmptyState icon={MessageSquare} title="No conversations yet" description="Your test conversations appear here." className="py-8" />
    );
  } else {
    body = (
      <div onKeyDown={onListKeyDown}>
        {groups.map((group) => (
          <section key={group.key} aria-labelledby={`conversation-group-${group.key}`} className="pb-2">
            <h3 id={`conversation-group-${group.key}`} className="sticky top-0 z-10 bg-canvas px-2.5 pt-2 pb-1 text-xs font-medium text-fg-muted">
              {group.label}
            </h3>
            <ul className="space-y-px" aria-labelledby={`conversation-group-${group.key}`}>
              {group.items.map((item) => (
                <ConversationRow
                  key={item.id}
                  item={item}
                  basePath={basePath}
                  active={item.id === activeId}
                  tabbable={item.id === tabbableId}
                  query={filters.q}
                  editing={item.id === editingId}
                  labelsAvailable={labelsAvailable}
                  onNavigate={onNavigate}
                  onFocusRow={setFocusId}
                  onStartRename={setEditingId}
                  onEndRename={endRename}
                  onRename={actions.rename}
                  onTogglePin={actions.togglePin}
                  onEditLabels={onEditLabels}
                  onDelete={actions.remove}
                />
              ))}
            </ul>
          </section>
        ))}
        {hasNextPage ? (
          <div ref={sentinelRef}>
            {isFetchingNextPage ? (
              <div role="status" aria-label="Loading more conversations">
                <SkeletonRows count={3} />
              </div>
            ) : typeof IntersectionObserver === 'undefined' ? (
              <Button size="sm" variant="ghost" className="mx-1" onClick={() => void fetchNextPage()}>
                Load more
              </Button>
            ) : null}
          </div>
        ) : null}
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0 flex-col bg-canvas">
      <h2 className="sr-only">Conversations</h2>
      {/* The same 48 px as the chat's top bar, so both share one line. */}
      <div className="flex h-12 shrink-0 items-center gap-1 border-b border-border-subtle pr-2 pl-4">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-fg-muted" aria-hidden />
          <input
            ref={searchRef}
            type="search"
            value={search.input}
            onChange={(event) => search.setInput(event.target.value)}
            onKeyDown={onSearchKeyDown}
            aria-label="Search conversations"
            aria-keyshortcuts={SEARCH_SHORTCUT.aria}
            placeholder="Search"
            className="peer h-8 w-full rounded-md border border-border bg-surface pr-12 pl-8 text-sm text-fg placeholder:text-fg-muted focus:border-focus focus:ring-1 focus:ring-focus focus:outline-none [&::-webkit-search-cancel-button]:hidden"
          />
          {search.input ? (
            <button
              type="button"
              onClick={() => {
                search.clear();
                searchRef.current?.focus();
              }}
              aria-label="Clear search text"
              className="absolute top-1/2 right-1 flex size-6 -translate-y-1/2 items-center justify-center rounded-sm text-fg-muted hover:bg-fill-hover hover:text-fg"
            >
              <X className="size-3.5" aria-hidden />
            </button>
          ) : (
            <span className="pointer-events-none absolute top-1/2 right-2 hidden -translate-y-1/2 peer-focus:hidden md:block">
              <Kbd>{modKey()}K</Kbd>
            </span>
          )}
        </div>
        {labelsAvailable ? <LabelFilter value={filter} onChange={onFilterChange} labels={labelList} /> : null}
        <Button
          variant="ghost"
          size="icon-sm"
          icon={SquarePen}
          aria-label="New chat"
          aria-keyshortcuts={NEW_CHAT_SHORTCUT.aria}
          title={`New chat (${NEW_CHAT_SHORTCUT.label})`}
          onClick={onNewChat}
        />
      </div>
      <ActiveFilterChip value={filter} labels={labelList} onClear={() => onFilterChange(NO_FILTER)} />
      <p className="sr-only" aria-live="polite">
        {filtered && !list.isPending ? `${ordered.length}${hasNextPage ? ' or more' : ''} conversations found` : ''}
      </p>
      <div ref={setScroller} className="min-h-0 flex-1 overflow-y-auto px-2 pb-3" aria-busy={list.isFetching && !list.isFetchingNextPage}>
        {body}
      </div>
      {labelItem ? (
        <LabelPicker
          open
          onClose={() => setLabelTarget(null)}
          anchor={labelTarget?.anchor}
          labels={labelList}
          value={labelItem.labels}
          onChange={(next) => actions.setLabels(labelItem, next)}
        />
      ) : null}
    </div>
  );
}
