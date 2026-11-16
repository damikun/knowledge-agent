import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query';
import { ApiError, asArray, call, isRecord } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import type { Guid } from '../../lib/types';
import { conversationKeys } from '../chat/api';
import { mergeMessages } from '../chat/timeline';
import type { ConversationChannel, ConversationDto, ConversationMessageDto, ConversationStatus } from '../chat/types';
import { toConversationLabels } from '../labels/api';
import { toConversationContact } from '../people/api';
import { listKey, toSearchQuery } from './inboxState';
import type {
  ConversationAssignee,
  ConversationFeedbackItem,
  ConversationHistory,
  ConversationSearchItem,
  ConversationSearchPage,
  HandoverDto,
  HandoverSource,
  HandoverStatus,
  InboxConversationDetail,
  InboxConversationDto,
  InboxCounts,
  InboxFilters,
  InboxState,
  InboxView,
  Segment,
  StaffMember,
} from './types';

export const inboxKeys = {
  all: ['conversations', 'inbox'] as const,
  list: (filters: InboxFilters) => ['conversations', 'inbox', 'list', filters] as const,
  waiting: ['conversations', 'inbox', 'waiting'] as const,
  searches: ['conversations', 'inbox', 'search'] as const,
  search: (state: InboxState) => ['conversations', 'inbox', 'search', listKey(state)] as const,
  allCounts: ['conversations', 'inbox', 'counts'] as const,
  counts: (filters: CountFilters) => ['conversations', 'inbox', 'counts', filters] as const,
  history: (id: Guid) => ['conversations', id, 'history'] as const,
  handover: (id: Guid) => ['conversations', id, 'handover'] as const,
  staff: ['staff'] as const,
  segments: ['segments'] as const,
};

const INBOX_PAGE_SIZE = 100;
const VIEW_PAGE_SIZE = 50;
const INBOX_REFRESH_MS = 10_000;
const WAITING_REFRESH_MS = 30_000;

const STATUSES: readonly ConversationStatus[] = ['ai', 'handover_pending', 'human', 'closed'];
const HANDOVER_STATUSES: readonly HandoverStatus[] = ['pending', 'active', 'resolved', 'cancelled', 'expired'];
const HANDOVER_SOURCES: readonly HandoverSource[] = ['agent', 'visitor', 'staff'];

export interface CountFilters {
  channel: InboxState['channel'];
  widgetId: string;
  contactId?: string;
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

export function isConversationStatus(value: unknown): value is ConversationStatus {
  return STATUSES.some((status) => status === value);
}

function toConversationStatus(value: unknown): ConversationStatus {
  return isConversationStatus(value) ? value : 'ai';
}

function toChannel(value: unknown): ConversationChannel {
  return value === 'portal' || value === 'api' || value === 'mcp' ? value : 'widget';
}

function toInboxConversation(value: unknown): InboxConversationDto {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    title: typeof dto.title === 'string' ? dto.title : '',
    turnCount: typeof dto.turnCount === 'number' ? dto.turnCount : 0,
    createdAt: String(dto.createdAt ?? ''),
    updatedAt: String(dto.updatedAt ?? ''),
    channel: toChannel(dto.channel),
    status: toConversationStatus(dto.status),
    agentId: text(dto.agentId),
    widgetId: text(dto.widgetId),
    preview: text(dto.preview),
    handoverStatus: HANDOVER_STATUSES.find((status) => status === dto.handoverStatus) ?? null,
    pinned: dto.pinned === true,
    labels: toConversationLabels(dto.labels),
  };
}

export function conversationItems(value: unknown): unknown[] {
  return Array.isArray(value) ? value : isRecord(value) ? asArray<unknown>(value.items) : [];
}

function toHandover(value: unknown): HandoverDto | null {
  if (!isRecord(value) || typeof value.id !== 'string') {
    return null;
  }
  return {
    id: value.id,
    conversationId: String(value.conversationId ?? ''),
    channel: typeof value.channel === 'string' ? value.channel : 'portal',
    status: HANDOVER_STATUSES.find((status) => status === value.status) ?? 'pending',
    source: HANDOVER_SOURCES.find((source) => source === value.source) ?? 'agent',
    reason: text(value.reason),
    summary: text(value.summary),
    assignee: text(value.assignee),
    requestedAt: String(value.requestedAt ?? ''),
    acceptedAt: text(value.acceptedAt),
    resolvedAt: text(value.resolvedAt),
    lastError: text(value.lastError),
  };
}

export function asInboxConversation(conversation: ConversationDto): InboxConversationDetail {
  const extra = conversation as Partial<InboxConversationDetail> & { contact?: unknown; createdBy?: unknown };
  const creator = isRecord(extra.createdBy) && typeof extra.createdBy.id === 'string' ? extra.createdBy : null;
  return {
    ...conversation,
    widgetId: extra.widgetId ?? null,
    visitorId: extra.visitorId ?? null,
    pinned: extra.pinned === true,
    contact: toConversationContact(extra.contact),
    createdBy: creator ? { id: String(creator.id), name: text(creator.name) ?? String(creator.id) } : null,
    assignee: toAssignee((extra as { assignee?: unknown }).assignee),
  };
}

function toAssignee(value: unknown): ConversationAssignee | null {
  return isRecord(value) && typeof value.name === 'string' && value.name ? { id: text(value.id), name: value.name } : null;
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function toSearchItem(value: unknown): ConversationSearchItem {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    title: typeof dto.title === 'string' ? dto.title : '',
    channel: toChannel(dto.channel),
    status: toConversationStatus(dto.status),
    agentId: text(dto.agentId),
    widgetId: text(dto.widgetId),
    createdAt: String(dto.createdAt ?? ''),
    updatedAt: String(dto.updatedAt ?? ''),
    messageCount: count(dto.messageCount),
    preview: text(dto.preview),
    match: text(dto.match),
    handoverStatus: HANDOVER_STATUSES.find((status) => status === dto.handoverStatus) ?? null,
    rating: typeof dto.rating === 'number' ? dto.rating : null,
    thumbsDown: count(dto.thumbsDown),
    pinned: dto.pinned === true,
    labels: toConversationLabels(dto.labels),
    contact: toConversationContact(dto.contact),
    assignee: toAssignee(dto.assignee),
    unread: dto.unread === true,
  };
}

function toFeedback(value: unknown): ConversationFeedbackItem {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    messageId: text(dto.messageId),
    kind: dto.kind === 'conversation' || dto.kind === 'agent' ? dto.kind : 'answer',
    rating: count(dto.rating),
    comment: text(dto.comment),
    reasons: asArray<unknown>(dto.reasons).filter((reason): reason is string => typeof reason === 'string'),
    author: dto.author === 'staff' ? 'staff' : 'visitor',
    createdAt: String(dto.createdAt ?? ''),
  };
}

function viewCounts(dto: Record<string, unknown>): Record<InboxView, number> {
  return {
    mine: count(dto.mine),
    waiting: count(dto.handoverPending),
    unassigned: count(dto.unassigned),
    ai: count(dto.ai),
    closed: count(dto.closed),
    all: count(dto.all),
  };
}

function toCounts(value: unknown): InboxCounts {
  const dto = isRecord(value) ? value : {};
  return {
    views: viewCounts(dto),
    unread: viewCounts(isRecord(dto.unread) ? dto.unread : {}),
    pinned: count(dto.pinned),
    labels: new Map(
      asArray<unknown>(dto.labels).flatMap((item) =>
        isRecord(item) && typeof item.labelId === 'string' ? [[item.labelId.toLowerCase(), count(item.count)] as const] : [],
      ),
    ),
  };
}

export const inboxApi = {
  listConversations: async (filters: InboxFilters, signal?: AbortSignal) =>
    conversationItems(
      await call(endpoints.conversations.list, {
        query: { channel: 'widget', status: filters.status, widgetId: filters.widgetId, limit: INBOX_PAGE_SIZE },
        signal,
      }),
    ).map(toInboxConversation),
  getHandover: async (id: Guid, signal?: AbortSignal) => toHandover(await call(endpoints.conversations.handover, { params: { id }, signal })),
  join: async (id: Guid) => toHandover(await call(endpoints.conversations.join, { params: { id } })),
  reply: (id: Guid, message: string): Promise<ConversationMessageDto> => call(endpoints.conversations.reply, { params: { id }, body: { text: message } }),
  returnToAgent: (id: Guid): Promise<void> => call(endpoints.conversations.returnToAgent, { params: { id } }),
  close: (id: Guid): Promise<void> => call(endpoints.conversations.close, { params: { id } }),
  assign: async (id: Guid, userId: string | null) => {
    const dto = await call(endpoints.conversations.assign, { params: { id }, body: { userId } });
    return toAssignee(isRecord(dto) ? dto.assignee : null);
  },
  markRead: (id: Guid, sequence?: number) =>
    call(endpoints.conversations.markRead, { params: { id }, body: sequence === undefined ? {} : { sequence } }),
  markUnread: (id: Guid) => call(endpoints.conversations.markUnread, { params: { id } }),
  staff: async (signal?: AbortSignal): Promise<StaffMember[]> =>
    asArray<unknown>(await call(endpoints.team.staff, { signal })).flatMap((item) =>
      isRecord(item) && typeof item.id === 'string' && typeof item.name === 'string'
        ? [{ id: item.id, name: item.name, email: text(item.email), role: typeof item.role === 'string' ? item.role : 'support' }]
        : [],
    ),
  segments: async (signal?: AbortSignal): Promise<Segment[]> => {
    try {
      const dto = await call(endpoints.segments.list, { signal });
      return (Array.isArray(dto) ? dto : isRecord(dto) ? asArray<unknown>(dto.items) : []).flatMap((item) =>
        isRecord(item) && typeof item.id === 'string' && typeof item.name === 'string' && item.name ? [{ id: item.id.toLowerCase(), name: item.name }] : [],
      );
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return [];
      }
      throw error;
    }
  },
  setPinned: (id: Guid, pinned: boolean) => call(endpoints.conversations.update, { params: { id }, body: { pinned } }),
  search: async (state: InboxState, cursor: string | null, signal?: AbortSignal): Promise<ConversationSearchPage> => {
    const dto = await call(endpoints.conversations.search, { query: toSearchQuery(state, cursor, VIEW_PAGE_SIZE), signal });
    const page = isRecord(dto) ? dto : {};
    return { items: asArray<unknown>(page.items).map(toSearchItem), nextCursor: text(page.nextCursor) };
  },
  counts: async (filters: CountFilters, signal?: AbortSignal) =>
    toCounts(
      await call(endpoints.conversations.counts, {
        query: { channel: filters.channel || undefined, widgetId: filters.widgetId || undefined, contactId: filters.contactId || undefined },
        signal,
      }),
    ),
  history: async (id: Guid, signal?: AbortSignal): Promise<ConversationHistory> => {
    const dto = await call(endpoints.conversations.history, { params: { id }, signal });
    const history = isRecord(dto) ? dto : {};
    return {
      feedback: asArray<unknown>(history.feedback).map(toFeedback),
      handovers: asArray<unknown>(history.handovers).flatMap((item) => toHandover(item) ?? []),
      labels: toConversationLabels(history.labels),
    };
  },
};

export function useInboxSearch(state: InboxState) {
  return useInfiniteQuery({
    queryKey: inboxKeys.search(state),
    queryFn: ({ signal, pageParam }) => inboxApi.search(state, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    placeholderData: keepPreviousData,
    refetchInterval: INBOX_REFRESH_MS,
    refetchOnWindowFocus: true,
  });
}

export function useInboxCounts(filters: CountFilters) {
  return useQuery({
    queryKey: inboxKeys.counts(filters),
    queryFn: ({ signal }) => inboxApi.counts(filters, signal),
    refetchInterval: INBOX_REFRESH_MS,
    refetchOnWindowFocus: true,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useConversationHistory(id: Guid | null) {
  return useQuery({
    queryKey: inboxKeys.history(id ?? ''),
    queryFn: ({ signal }) => inboxApi.history(id!, signal),
    enabled: Boolean(id),
  });
}

export function useInboxConversations(filters: InboxFilters) {
  return useQuery({
    queryKey: inboxKeys.list(filters),
    queryFn: ({ signal }) => inboxApi.listConversations(filters, signal),
    refetchInterval: INBOX_REFRESH_MS,
    refetchOnWindowFocus: true,
    placeholderData: keepPreviousData,
  });
}

export function useWaitingCount(enabled = true): number | undefined {
  const waiting = useQuery({
    queryKey: inboxKeys.waiting,
    queryFn: async ({ signal }) => (await inboxApi.listConversations({ status: 'handover_pending', widgetId: '' }, signal)).length,
    refetchInterval: WAITING_REFRESH_MS,
    refetchOnWindowFocus: true,
    enabled,
  });
  return waiting.data;
}

export function useHandover(id: Guid) {
  return useQuery({ queryKey: inboxKeys.handover(id), queryFn: ({ signal }) => inboxApi.getHandover(id, signal) });
}

export function usePinConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, pinned }: { id: Guid; pinned: boolean }) => inboxApi.setPinned(id, pinned),
    onMutate: async ({ id, pinned }) => {
      await queryClient.cancelQueries({ queryKey: inboxKeys.searches });
      await queryClient.cancelQueries({ queryKey: conversationKeys.detail(id), exact: true });
      const lists = queryClient.getQueriesData<InfiniteData<ConversationSearchPage>>({ queryKey: inboxKeys.searches });
      const detail = queryClient.getQueryData<ConversationDto>(conversationKeys.detail(id));
      queryClient.setQueriesData<InfiniteData<ConversationSearchPage>>({ queryKey: inboxKeys.searches }, (old) =>
        old
          ? { ...old, pages: old.pages.map((page) => ({ ...page, items: page.items.map((item) => (item.id === id ? { ...item, pinned } : item)) })) }
          : old,
      );
      if (detail) {
        queryClient.setQueryData(conversationKeys.detail(id), { ...detail, pinned });
      }
      return { lists, detail };
    },
    onError: (_error, { id }, context) => {
      for (const [key, data] of context?.lists ?? []) {
        queryClient.setQueryData(key, data);
      }
      if (context?.detail) {
        queryClient.setQueryData(conversationKeys.detail(id), context.detail);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: inboxKeys.all });
      void queryClient.invalidateQueries({ queryKey: conversationKeys.lists });
    },
  });
}

export function useSegments() {
  return useQuery({ queryKey: inboxKeys.segments, queryFn: ({ signal }) => inboxApi.segments(signal), staleTime: 60_000, retry: false });
}

export function useStaff(enabled = true) {
  return useQuery({ queryKey: inboxKeys.staff, queryFn: ({ signal }) => inboxApi.staff(signal), enabled, staleTime: 60_000, retry: false });
}

function patchRows(queryClient: ReturnType<typeof useQueryClient>, id: Guid, patch: Partial<ConversationSearchItem>) {
  queryClient.setQueriesData<InfiniteData<ConversationSearchPage>>({ queryKey: inboxKeys.searches }, (old) =>
    old ? { ...old, pages: old.pages.map((page) => ({ ...page, items: page.items.map((item) => (item.id === id ? { ...item, ...patch } : item)) })) } : old,
  );
}

function patchConversation(
  queryClient: ReturnType<typeof useQueryClient>,
  id: Guid,
  patch: Partial<ConversationSearchItem & InboxConversationDetail>,
) {
  patchRows(queryClient, id, patch);
  const detail = queryClient.getQueryData<ConversationDto>(conversationKeys.detail(id));
  if (detail) {
    queryClient.setQueryData(conversationKeys.detail(id), { ...detail, ...patch });
  }
}

export function useAssignConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, assignee }: { id: Guid; assignee: ConversationAssignee | null }) => inboxApi.assign(id, assignee?.id ?? null),
    onMutate: async ({ id, assignee }) => {
      await queryClient.cancelQueries({ queryKey: inboxKeys.searches });
      patchConversation(queryClient, id, { assignee });
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: inboxKeys.all });
      void queryClient.invalidateQueries({ queryKey: conversationKeys.lists });
    },
  });
}

export function useReadState() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, unread }: { id: Guid; unread: boolean }) => (unread ? inboxApi.markUnread(id) : inboxApi.markRead(id)),
    onMutate: async ({ id, unread }) => {
      await queryClient.cancelQueries({ queryKey: inboxKeys.searches });
      patchConversation(queryClient, id, { unread });
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: inboxKeys.allCounts });
      void queryClient.invalidateQueries({ queryKey: inboxKeys.searches });
    },
  });
}

export function useMarkReadUpTo(id: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (sequence: number) => inboxApi.markRead(id, sequence),
    onMutate: () => patchRows(queryClient, id, { unread: false }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.allCounts }),
  });
}

function useRefreshConversation(id: Guid) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: conversationKeys.detail(id) });
    void queryClient.invalidateQueries({ queryKey: inboxKeys.all });
  };
}

export function useHandoverActions(id: Guid) {
  const queryClient = useQueryClient();
  const refresh = useRefreshConversation(id);
  const join = useMutation({
    mutationFn: () => inboxApi.join(id),
    onSuccess: (taken) => {
      queryClient.setQueryData(inboxKeys.handover(id), taken);
      refresh();
    },
  });
  const giveBack = useMutation({ mutationFn: () => inboxApi.returnToAgent(id), onSuccess: refresh });
  const close = useMutation({ mutationFn: () => inboxApi.close(id), onSuccess: refresh });
  return { join, giveBack, close };
}

export function useReplyToConversation(id: Guid) {
  const queryClient = useQueryClient();
  const refresh = useRefreshConversation(id);
  return useMutation({
    mutationFn: (text: string) => inboxApi.reply(id, text),
    onSuccess: (message: ConversationMessageDto | undefined) => {
      if (message && typeof message.sequence === 'number') {
        queryClient.setQueryData<ConversationDto>(conversationKeys.detail(id), (old) =>
          old ? { ...old, messages: mergeMessages(old.messages, [message]) } : old,
        );
      }
      refresh();
    },
  });
}
