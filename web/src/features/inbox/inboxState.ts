import { Archive, Bot, Hand, Inbox, UserRound, UserRoundX, type LucideIcon } from 'lucide-react';
import type { ConversationSort, InboxState, InboxView } from './types';

export interface InboxViewDefinition {
  id: InboxView;
  label: string;
  icon: LucideIcon;
  /** Primary views are the team's work; secondary ones are for looking things up. */
  group: 'primary' | 'secondary';
  /** One status or a comma-separated list; null for any. */
  status: string | null;
  assignee?: 'me' | 'none';
}

const OPEN = 'ai,handover_pending,human';
const NEEDS_PERSON = 'handover_pending,human';

export const INBOX_VIEWS: readonly InboxViewDefinition[] = [
  { id: 'mine', label: 'Mine', icon: UserRound, group: 'primary', status: OPEN, assignee: 'me' },
  { id: 'waiting', label: 'Waiting for a person', icon: Hand, group: 'primary', status: 'handover_pending' },
  { id: 'unassigned', label: 'Unassigned', icon: UserRoundX, group: 'primary', status: NEEDS_PERSON, assignee: 'none' },
  { id: 'ai', label: 'Handled by AI', icon: Bot, group: 'secondary', status: 'ai' },
  { id: 'closed', label: 'Closed', icon: Archive, group: 'secondary', status: 'closed' },
  { id: 'all', label: 'All', icon: Inbox, group: 'secondary', status: null },
];

// Legacy links: ?status= from the old Conversations page, and the removed With a person and Pinned views.
const VIEW_BY_STATUS: Readonly<Record<string, InboxView>> = { handover_pending: 'waiting', ai: 'ai', closed: 'closed' };
const SORTS: readonly ConversationSort[] = ['updated_desc', 'updated_asc', 'created_desc', 'created_asc'];

export const DEFAULT_STATE: InboxState = {
  view: 'all',
  labelId: '',
  q: '',
  channel: '',
  widgetId: '',
  handover: '',
  rating: '',
  thumbsDown: false,
  period: '',
  sort: 'updated_desc',
  contactId: '',
  assignee: '',
  pinned: false,
  segmentId: '',
  conversationId: null,
  only: '',
  viewChosen: true,
};

function viewDefinition(view: InboxView): InboxViewDefinition {
  return INBOX_VIEWS.find((candidate) => candidate.id === view) ?? INBOX_VIEWS.find((candidate) => candidate.id === 'all')!;
}

/** Legacy ?status= links pick the matching view. */
export function readInboxState(params: URLSearchParams): InboxState {
  const view = params.get('view');
  const channel = params.get('channel');
  const handover = params.get('handover');
  const rating = params.get('rating');
  const period = params.get('period');
  const sort = params.get('sort');
  return {
    view: INBOX_VIEWS.find((candidate) => candidate.id === view)?.id ?? VIEW_BY_STATUS[params.get('status') ?? ''] ?? 'all',
    labelId: params.get('label')?.toLowerCase() ?? '',
    q: params.get('q') ?? '',
    channel: channel === 'portal' || channel === 'widget' || channel === 'api' || channel === 'mcp' ? channel : '',
    widgetId: params.get('widget')?.toLowerCase() ?? '',
    handover: handover === 'yes' || handover === 'no' ? handover : '',
    rating: rating === 'good' || rating === 'poor' ? rating : '',
    thumbsDown: params.get('thumbs') === 'down',
    period: period === '7' || period === '30' || period === '90' ? period : '',
    sort: SORTS.find((candidate) => candidate === sort) ?? 'updated_desc',
    contactId: params.get('person')?.toLowerCase() ?? '',
    assignee: params.get('assignee') ?? '',
    pinned: params.get('pinned') === '1' || view === 'pinned',
    segmentId: params.get('segment')?.toLowerCase() ?? '',
    conversationId: params.get('c') ?? params.get('open') ?? params.get('only'),
    only: params.get('only') ?? '',
    viewChosen: params.has('view') || params.has('status'),
  };
}

/**
 * Set when another page opened one conversation (or one contact's). The view tabs then step back, so "All"
 * never claims a narrowed list.
 */
export function inboxContext(state: InboxState): 'only' | 'contact' | null {
  if (state.only) {
    return 'only';
  }
  return state.contactId && !state.viewChosen ? 'contact' : null;
}

/** For pages without a list of their own: the Inbox shows only this conversation, with a chip to see the rest. */
export function onlyConversationPath(conversationId: string): string {
  return `/inbox?only=${encodeURIComponent(conversationId)}`;
}

export function toInboxSearch(state: InboxState): URLSearchParams {
  const search = new URLSearchParams();
  const contextual = inboxContext(state) !== null;
  // "only" already includes the open conversation.
  const open = state.only && state.conversationId?.toLowerCase() === state.only.toLowerCase() ? '' : (state.conversationId ?? '');
  const entries: Array<[string, string]> = [
    ['only', state.only],
    ['view', contextual ? '' : state.view],
    ['label', state.labelId],
    ['q', state.q.trim()],
    ['channel', state.channel],
    ['widget', state.widgetId],
    ['handover', state.handover],
    ['rating', state.rating],
    ['thumbs', state.thumbsDown ? 'down' : ''],
    ['period', state.period],
    ['sort', state.sort === 'updated_desc' ? '' : state.sort],
    ['person', state.contactId],
    ['assignee', state.assignee],
    ['pinned', state.pinned ? '1' : ''],
    ['segment', state.segmentId],
    ['c', open],
  ];
  for (const [key, value] of entries) {
    if (value) {
      search.set(key, value);
    }
  }
  return search;
}

/** The pin toggle counts as a filter. */
export function activeFilterCount(state: InboxState): number {
  return [
    state.channel,
    state.widgetId,
    state.labelId,
    state.handover,
    state.rating,
    state.thumbsDown,
    state.period,
    state.contactId,
    state.assignee,
    state.segmentId,
    state.pinned,
  ].filter(Boolean).length;
}

/** Keeps the view, words, sort and open conversation. */
export function withoutFilters(state: InboxState): InboxState {
  return {
    ...state,
    channel: '',
    widgetId: '',
    labelId: '',
    handover: '',
    rating: '',
    thumbsDown: false,
    period: '',
    contactId: '',
    assignee: '',
    segmentId: '',
    pinned: false,
  };
}

function startOfPeriod(period: InboxState['period'], now: number): string | undefined {
  if (!period) {
    return undefined;
  }
  const start = new Date(now);
  start.setHours(0, 0, 0, 0);
  start.setDate(start.getDate() - Number(period) + 1);
  return start.toISOString();
}

/** The list's dependencies, so opening a conversation doesn't refetch the list. */
export function listKey(state: InboxState) {
  const { conversationId: _open, viewChosen: _chosen, ...rest } = state;
  return { ...rest, q: rest.q.trim() };
}

/** Portal conversations (playground tests) only with the portal channel. */
export function toSearchQuery(state: InboxState, cursor: string | null, limit: number, now = Date.now()) {
  const [sort, order] = state.sort.split('_');
  const view = viewDefinition(state.view);
  return {
    q: state.q.trim() || undefined,
    status: view.status ?? undefined,
    pinned: state.pinned || undefined,
    // The view's assignee wins, so Mine always lists the user's own.
    assignee: view.assignee ?? (state.assignee || undefined),
    labelId: state.labelId || undefined,
    channel: state.channel || undefined,
    widgetId: state.widgetId || undefined,
    contactId: state.contactId || undefined,
    segmentId: state.segmentId || undefined,
    handover: state.handover === 'yes' ? true : state.handover === 'no' ? false : undefined,
    minRating: state.rating === 'good' ? 4 : undefined,
    maxRating: state.rating === 'poor' ? 2 : undefined,
    thumbsDown: state.thumbsDown || undefined,
    from: startOfPeriod(state.period, now),
    sort,
    order,
    cursor: cursor ?? undefined,
    limit,
  };
}
