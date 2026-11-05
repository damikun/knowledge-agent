import { clsx } from 'clsx';
import { Inbox, Pin, Search, SearchX } from 'lucide-react';
import { useId, useMemo, type ReactNode } from 'react';
import { Button } from '../../../components/ui/Button';
import { EmptyState, ErrorMessage, Spinner } from '../../../components/ui/feedback';
import { useLoadMoreWhenVisible } from '../../../lib/hooks/useLoadMoreWhenVisible';
import { useMe } from '../../auth/api';
import { useConversation } from '../../chat/api';
import type { Label } from '../../labels/types';
import { asInboxConversation, useAssignConversation, useInboxSearch, usePinConversation, useReadState } from '../api';
import { activeFilterCount, withoutFilters } from '../inboxState';
import type { ConversationSearchItem, InboxConversationDetail, InboxState } from '../types';
import { ActiveFilterChip } from './ActiveFilterChip';
import { ConversationRow, RowActions } from './ConversationRow';
import { FilterBar } from './FilterBar';
import { SortMenu } from './SortMenu';
import { useSearchInput } from './useSearchInput';

const NO_WORDS: readonly string[] = [];

export interface ConversationListProps {
  state: InboxState;
  onChange: (next: InboxState) => void;
  /** Names the list for screen readers; the view tabs show it visually. */
  header: ReactNode;
  linkTo: (conversationId: string) => string;
  /** By lower-case id; undefined for support users, who can't read widgets. */
  widgetNames: ReadonlyMap<string, string> | undefined;
  canFilterByWidget: boolean;
  /** Empty hides the label filter, e.g. on an API without labels. */
  labels: readonly Label[];
  onManageLabels?: () => void;
  /** For an empty view without search or filters. */
  emptyText: { title: string; description: string };
}

// Built from the conversation itself, since search can't look up an id.
function onlyRow(conversation: InboxConversationDetail): ConversationSearchItem {
  const last = conversation.messages.filter((message) => !message.staffOnly && message.text).at(-1);
  return {
    id: conversation.id,
    title: conversation.title,
    channel: conversation.channel,
    status: conversation.status,
    agentId: conversation.agentId,
    widgetId: conversation.widgetId,
    createdAt: conversation.createdAt,
    updatedAt: conversation.updatedAt,
    messageCount: conversation.messages.length,
    preview: last?.text ?? null,
    match: null,
    handoverStatus: null,
    rating: null,
    thumbsDown: 0,
    pinned: conversation.pinned,
    labels: [],
    contact: conversation.contact,
    assignee: conversation.assignee,
    unread: false,
  };
}

/** The list holds only the conversation a page opened; removing the chip shows the normal view. */
function OnlyThisConversation({
  state,
  onChange,
  linkTo,
  widgetNames,
}: Pick<ConversationListProps, 'state' | 'onChange' | 'linkTo' | 'widgetNames'>) {
  const conversation = useConversation(state.only);
  return (
    <div className="flex h-full min-h-0 w-full flex-col">
      <div className="flex min-h-12 items-center border-b border-border-subtle px-3 py-2">
        <ul aria-label="Active filters" className="flex max-w-full flex-wrap items-center gap-1.5">
          <ActiveFilterChip text="Only this conversation" onRemove={() => onChange({ ...state, only: '' })} removeTitle="Show every conversation">
            Only this conversation
          </ActiveFilterChip>
        </ul>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-2 py-2">
        {conversation.isPending ? (
          <div className="px-2 py-3">
            <Spinner label="Loading conversation" />
          </div>
        ) : conversation.isError ? (
          <ErrorMessage error={conversation.error} title="Could not load the conversation" className="mx-1" />
        ) : (
          <ul className="space-y-0.5" aria-label="Conversations">
            <li>
              <ConversationRow
                item={onlyRow(asInboxConversation(conversation.data))}
                words={NO_WORDS}
                active={state.only.toLowerCase() === state.conversationId?.toLowerCase()}
                to={linkTo(state.only)}
                widgetNames={widgetNames}
                byCreated={false}
              />
            </li>
          </ul>
        )}
      </div>
    </div>
  );
}

export function ConversationList(props: ConversationListProps) {
  return props.state.only ? <OnlyThisConversation {...props} /> : <ViewList {...props} />;
}

function ViewList({ state, onChange, header, linkTo, widgetNames, canFilterByWidget, labels, onManageLabels, emptyText }: ConversationListProps) {
  const ids = useId();
  const search = useInboxSearch(state);
  const { input, setInput } = useSearchInput(state.q, (q) => onChange({ ...state, q }));
  const narrowed = activeFilterCount(state);
  const words = useMemo(() => state.q.trim().split(/\s+/).filter(Boolean), [state.q]);
  // Stable callbacks, so a list refresh re-renders only changed rows.
  const { mutate: pin } = usePinConversation();
  const { mutate: setUnread } = useReadState();
  const { mutate: assign } = useAssignConversation();
  const meData = useMe().data;
  const meId = meData?.id;
  const meName = meData?.name;
  const me = useMemo(() => (meId ? { id: meId, name: meName ?? '' } : null), [meId, meName]);
  // Loads the next page when the end scrolls into view.
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = search;
  const sentinelRef = useLoadMoreWhenVisible({ hasNextPage, isFetchingNextPage, fetchNextPage });

  const items = search.data?.pages.flatMap((page) => page.items) ?? [];
  const clearAll = () => {
    setInput('');
    onChange({ ...withoutFilters(state), q: '' });
  };

  return (
    <div className="flex h-full min-h-0 w-full flex-col">
      <div className="space-y-1.5 border-b border-border-subtle px-3 pt-2 pb-2.5">
        {header}
        <div className="flex items-center gap-1">
          <form
            role="search"
            onSubmit={(event) => {
              event.preventDefault();
              onChange({ ...state, q: input.trim() });
            }}
            className="relative min-w-0 flex-1"
          >
            <label htmlFor={`${ids}-q`} className="sr-only">
              Search conversations
            </label>
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
            <input
              id={`${ids}-q`}
              type="search"
              value={input}
              onChange={(event) => setInput(event.target.value)}
              placeholder="Search titles and messages"
              className="field pl-8"
              autoComplete="off"
            />
          </form>
          <Button
            variant="ghost"
            size="icon"
            icon={Pin}
            aria-label="Pinned only"
            aria-pressed={state.pinned}
            title={state.pinned ? 'Showing pinned conversations only' : 'Show pinned conversations only'}
            onClick={() => onChange({ ...state, pinned: !state.pinned })}
            className={clsx(state.pinned && 'bg-fill-active text-accent-icon')}
          />
          <SortMenu value={state.sort} onChange={(sort) => onChange({ ...state, sort })} />
        </div>
        <FilterBar
          state={state}
          onChange={onChange}
          widgetNames={widgetNames}
          canFilterByWidget={canFilterByWidget}
          labels={labels}
          onManageLabels={onManageLabels}
        />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-2 py-2" aria-busy={search.isFetching || undefined}>
        {search.isPending ? (
          <div className="px-2 py-3">
            <Spinner label="Loading conversations" />
          </div>
        ) : search.isError ? (
          <ErrorMessage error={search.error} title="Could not load the conversations" className="mx-1" />
        ) : items.length === 0 ? (
          state.pinned && narrowed === 1 && words.length === 0 ? (
            <EmptyState
              icon={Pin}
              title="No pinned conversations here"
              description="Pin a conversation from its menu to keep it at hand for the whole team."
              action={<Button onClick={() => onChange({ ...state, pinned: false })}>Show all conversations</Button>}
              className="py-8"
            />
          ) : state.contactId && narrowed === 1 && words.length === 0 ? (
            <EmptyState
              icon={Inbox}
              title="No conversations with this contact here"
              description="They may have opened the chat without writing, or their conversations are in another view."
              action={<Button onClick={clearAll}>Show every conversation</Button>}
              className="py-8"
            />
          ) : words.length > 0 || narrowed > 0 ? (
            <EmptyState
              icon={SearchX}
              title="No conversations match"
              description={words.length > 0 ? `Nothing matches “${state.q.trim()}”${narrowed ? ' with these filters' : ''}. Try fewer words.` : 'Nothing matches these filters.'}
              action={<Button onClick={clearAll}>Clear search and filters</Button>}
              className="py-8"
            />
          ) : (
            <EmptyState icon={Inbox} title={emptyText.title} description={emptyText.description} className="py-8" />
          )
        ) : (
          <>
            <ul className="space-y-0.5" aria-label="Conversations">
              {items.map((item) => (
                <li key={item.id} className="group relative">
                  <ConversationRow
                    item={item}
                    words={words}
                    active={item.id.toLowerCase() === state.conversationId?.toLowerCase()}
                    to={linkTo(item.id)}
                    widgetNames={widgetNames}
                    byCreated={state.sort.startsWith('created')}
                  />
                  <RowActions item={item} me={me} pin={pin} setUnread={setUnread} assign={assign} />
                </li>
              ))}
            </ul>
            <div ref={sentinelRef} className="flex justify-center py-2">
              {hasNextPage ? (
                <Button size="sm" variant="ghost" loading={isFetchingNextPage} onClick={() => void fetchNextPage()}>
                  Show more
                </Button>
              ) : null}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
