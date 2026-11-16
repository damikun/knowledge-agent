import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, type InfiniteData, type QueryClient } from '@tanstack/react-query';
import { ApiError, asArray, call, isRecord, networkError, toApiError, urlOf } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import { safeExternalUrl } from '../../lib/format';
import { isAbortError, sseRequest, type SseMessage } from '../../lib/sse';
import type { Guid } from '../../lib/types';
import { useSystemInfo } from '../system/api';
import {
  ChatEventTypes,
  type ChatEvent,
  type ChatEventType,
  type ChatRequestDto,
  type ConversationDto,
  type ConversationLabelDto,
  type ConversationListItem,
  type ConversationLiveEvent,
  type ConversationMessageDto,
  type ConversationPage,
  type ConversationStatus,
  type ConversationSummaryDto,
  type LabelColor,
  type LabelDto,
} from './types';

export interface ConversationListFilters {
  q: string;
  labelId: string;
  pinned: boolean;
}

export const EMPTY_LIST_FILTERS: ConversationListFilters = { q: '', labelId: '', pinned: false };

const CONVERSATION_PAGE_SIZE = 50;

export const conversationKeys = {
  all: ['conversations'] as const,
  lists: ['conversations', 'list'] as const,
  list: (filters: ConversationListFilters) => ['conversations', 'list', filters] as const,
  detail: (id: string) => ['conversations', id] as const,
};

const labelKeys = {
  all: ['labels'] as const,
};

const LABEL_COLORS: readonly string[] = ['gray', 'blue', 'green', 'amber', 'red', 'purple', 'teal', 'pink'] satisfies LabelColor[];

function labelColor(value: unknown): LabelColor {
  return typeof value === 'string' && LABEL_COLORS.includes(value) ? (value as LabelColor) : 'gray';
}

function normalizeLabels(value: unknown): ConversationLabelDto[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value
    .filter(isRecord)
    .flatMap((label) =>
      typeof label.id === 'string' && typeof label.name === 'string'
        ? [
            {
              id: label.id,
              name: label.name,
              color: labelColor(label.color),
              source: label.source === 'auto' ? ('auto' as const) : ('manual' as const),
            },
          ]
        : [],
    );
}

export function normalizeSummary(dto: ConversationSummaryDto): ConversationListItem {
  return { ...dto, title: typeof dto.title === 'string' ? dto.title : '', pinned: dto.pinned === true, labels: normalizeLabels(dto.labels) };
}

function compareConversations(a: ConversationListItem, b: ConversationListItem): number {
  if (a.pinned !== b.pinned) {
    return a.pinned ? -1 : 1;
  }
  return b.updatedAt.localeCompare(a.updatedAt);
}

export function toConversationPage(body: unknown, filters: ConversationListFilters): ConversationPage {
  if (isRecord(body) && Array.isArray(body.items)) {
    return {
      items: (body.items as ConversationSummaryDto[]).map(normalizeSummary),
      nextCursor: typeof body.nextCursor === 'string' && body.nextCursor ? body.nextCursor : null,
    };
  }
  const q = filters.q.trim().toLowerCase();
  const items = asArray<ConversationSummaryDto>(body)
    .map(normalizeSummary)
    .filter((item) => !q || item.title.toLowerCase().includes(q))
    .filter((item) => !filters.labelId || item.labels.some((label) => label.id === filters.labelId))
    .filter((item) => !filters.pinned || item.pinned)
    .sort(compareConversations);
  return { items, nextCursor: null };
}

function normalizeLabel(value: unknown): LabelDto | null {
  if (!isRecord(value) || typeof value.id !== 'string' || typeof value.name !== 'string') {
    return null;
  }
  return {
    id: value.id,
    name: value.name,
    color: labelColor(value.color),
    description: typeof value.description === 'string' && value.description ? value.description : null,
    autoApply: value.autoApply === true,
    conversationCount: typeof value.conversationCount === 'number' ? value.conversationCount : 0,
  };
}

const CONVERSATION_BUSY = 'conversation_busy';

export function isConversationBusy(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409 && error.problem?.code === CONVERSATION_BUSY;
}

function normalizeConversation(dto: ConversationDto): ConversationDto {
  return {
    ...dto,
    turns: Array.isArray(dto.turns) ? dto.turns : [],
    messages: Array.isArray(dto.messages) ? dto.messages : [],
    agentId: dto.agentId ?? null,
    channel: dto.channel ?? 'portal',
    status: dto.status ?? 'ai',
  };
}

export const chatApi = {
  listConversations: async (filters: ConversationListFilters, cursor: string | null, signal?: AbortSignal) =>
    toConversationPage(
      await call(endpoints.conversations.list, {
        query: {
          channel: 'portal',
          q: filters.q.trim(),
          labelId: filters.labelId,
          pinned: filters.pinned ? true : undefined,
          cursor,
          limit: CONVERSATION_PAGE_SIZE,
        },
        signal,
      }),
      filters,
    ),
  updateConversation: (id: Guid, patch: { title?: string; pinned?: boolean }) =>
    call(endpoints.conversations.update, { params: { id }, body: patch }),
  setLabels: (id: Guid, labelIds: Guid[]) =>
    call(endpoints.conversations.setLabels, { params: { id }, body: { labelIds } }),
  listLabels: async (signal?: AbortSignal) =>
    asArray<unknown>(await call(endpoints.labels.list, { signal })).flatMap((label) => normalizeLabel(label) ?? []),
  listLinkHosts: async (signal?: AbortSignal) => {
    const result = await call(endpoints.chat.linkHosts, { signal });
    return isRecord(result) ? asArray<unknown>(result.hosts).filter((host): host is string => typeof host === 'string') : [];
  },
  getConversation: async (id: Guid, signal?: AbortSignal) =>
    normalizeConversation(await call(endpoints.conversations.get, { params: { id }, signal })),
  deleteConversation: (id: Guid): Promise<void> => call(endpoints.conversations.remove, { params: { id } }),
  stopAnswer: async (id: Guid) => {
    const result: { stopped?: unknown } | undefined = await call(endpoints.conversations.stop, { params: { id } });
    return result?.stopped === true;
  },
};

const CHAT_EVENT_NAMES = new Set<string>(Object.values(ChatEventTypes));

function isChatEventType(name: string): name is ChatEventType {
  return CHAT_EVENT_NAMES.has(name);
}

function toChatEvent(event: string, payload: unknown): ChatEvent | null {
  if (!isChatEventType(event) || !isRecord(payload)) {
    return null;
  }
  const normalized = {
    ...payload,
    agent: typeof payload.agent === 'string' && payload.agent ? payload.agent : 'orchestrator',
    parentCallId: typeof payload.parentCallId === 'string' && payload.parentCallId ? payload.parentCallId : null,
  };
  return { event, data: normalized } as ChatEvent;
}

function parseJson(data: string): unknown {
  try {
    return JSON.parse(data);
  } catch {
    return undefined;
  }
}

export function parseChatEvent(event: string, data: string): ChatEvent | null {
  return toChatEvent(event, parseJson(data));
}

const CONVERSATION_STATUSES: readonly string[] = ['ai', 'handover_pending', 'human', 'closed'] satisfies ConversationStatus[];

export function parseConversationEvent({ event, data }: SseMessage): ConversationLiveEvent | null {
  const payload = parseJson(data);
  if (!isRecord(payload)) {
    return null;
  }
  switch (event) {
    case 'message': {
      if (typeof payload.sequence !== 'number' || typeof payload.author !== 'string') {
        return null;
      }
      const message = payload as unknown as ConversationMessageDto;
      return {
        type: 'message',
        message: {
          ...message,
          text: typeof message.text === 'string' ? message.text : '',
          citations: Array.isArray(message.citations) ? message.citations : [],
          turnId: message.turnId ?? null,
        },
      };
    }
    case 'turn': {
      const turnEvent = typeof payload.type === 'string' ? toChatEvent(payload.type, payload.data) : null;
      return turnEvent ? { type: 'turn', event: turnEvent } : null;
    }
    case 'conversation':
      return typeof payload.status === 'string' && CONVERSATION_STATUSES.includes(payload.status)
        ? { type: 'status', conversationId: String(payload.conversationId ?? ''), status: payload.status as ConversationStatus }
        : null;
    default:
      return null;
  }
}

function toStreamError(error: unknown): unknown {
  if (isAbortError(error) || error instanceof ApiError) {
    return error;
  }
  return error instanceof TypeError ? networkError(error) : error;
}

export async function* streamChat(request: ChatRequestDto, signal?: AbortSignal): AsyncGenerator<ChatEvent, void, undefined> {
  try {
    const target = endpoints.chat.send;
    const stream = sseRequest(urlOf(target), {
      method: target.method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
      ...(signal ? { signal } : {}),
      onErrorResponse: toApiError,
    });
    for await (const message of stream) {
      const parsed = parseChatEvent(message.event, message.data);
      if (parsed) {
        yield parsed;
      }
    }
  } catch (error) {
    throw toStreamError(error);
  }
}

export interface ConversationEventsRequest {
  after: number;
  resume?: boolean;
  signal?: AbortSignal;
  onOpen?: () => void;
}

export async function* streamConversationEvents(
  id: Guid,
  { after, resume = false, signal, onOpen }: ConversationEventsRequest,
): AsyncGenerator<ConversationLiveEvent, void, undefined> {
  try {
    const target = endpoints.conversations.events;
    const stream = sseRequest(urlOf(target, { params: { id }, query: resume ? undefined : { after } }), {
      method: target.method,
      ...(resume ? { headers: { 'Last-Event-ID': String(after) } } : {}),
      ...(signal ? { signal } : {}),
      ...(onOpen ? { onOpen: () => onOpen() } : {}),
      onErrorResponse: toApiError,
    });
    for await (const message of stream) {
      const parsed = parseConversationEvent(message);
      if (parsed) {
        yield parsed;
      }
    }
  } catch (error) {
    throw toStreamError(error);
  }
}

type ConversationListData = InfiniteData<ConversationPage, string | null>;

export function useConversationList(filters: ConversationListFilters) {
  return useInfiniteQuery({
    queryKey: conversationKeys.list(filters),
    queryFn: ({ signal, pageParam }) => chatApi.listConversations(filters, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    placeholderData: keepPreviousData,
  });
}

export function useLabels() {
  return useQuery({
    queryKey: labelKeys.all,
    queryFn: async ({ signal }) => {
      try {
        return { available: true, labels: await chatApi.listLabels(signal) };
      } catch (error) {
        if (error instanceof ApiError && (error.status === 404 || error.status === 405)) {
          return { available: false, labels: [] as LabelDto[] };
        }
        throw error;
      }
    },
    staleTime: 60_000,
  });
}

const NO_HOSTS: readonly string[] = [];

export function useLinkHosts(): readonly string[] {
  const query = useQuery({ queryKey: ['link-hosts'], queryFn: ({ signal }) => chatApi.listLinkHosts(signal), staleTime: 5 * 60_000, retry: false });
  return query.data ?? NO_HOSTS;
}

export function updateListItems(queryClient: QueryClient, change: (item: ConversationListItem) => ConversationListItem | null): void {
  queryClient.setQueriesData<ConversationListData>({ queryKey: conversationKeys.lists }, (data) =>
    data
      ? {
          ...data,
          pages: data.pages.map((page) => ({
            ...page,
            items: page.items.flatMap((item) => {
              const next = change(item);
              return next ? [next] : [];
            }),
          })),
        }
      : data,
  );
}

export function patchListItem(queryClient: QueryClient, id: string, patch: Partial<ConversationListItem>): void {
  updateListItems(queryClient, (item) => (item.id === id ? { ...item, ...patch } : item));
}

export function findListItem(queryClient: QueryClient, id: string): ConversationListItem | undefined {
  for (const [, data] of queryClient.getQueriesData<ConversationListData>({ queryKey: conversationKeys.lists })) {
    for (const page of data?.pages ?? []) {
      const hit = page.items.find((item) => item.id === id);
      if (hit) {
        return hit;
      }
    }
  }
  return undefined;
}

export function useConversation(id: string | null) {
  return useQuery({
    queryKey: conversationKeys.detail(id ?? ''),
    queryFn: ({ signal }) => chatApi.getConversation(id!, signal),
    enabled: Boolean(id),
  });
}

export function useLangfuseUrl(enabled: boolean): string | null {
  const info = useSystemInfo({ enabled });
  return info.data?.langfuseConfigured ? safeExternalUrl(info.data.langfuseUrl) : null;
}

export function useStopAnswer() {
  return useMutation({ mutationFn: (conversationId: Guid) => chatApi.stopAnswer(conversationId) });
}
