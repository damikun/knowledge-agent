import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../lib/api';
import type { Guid } from '../../lib/types';
import { conversationKeys } from '../chat/api';
import { encodeFilter } from './filters';
import type {
  AttributeRegistry,
  AttributeSource,
  AttributeType,
  AttributeValue,
  ContactAttribute,
  ContactCondition,
  ConversationContact,
  FilterAttribute,
  PersonAttribute,
  SaveSegmentRequest,
  Segment,
  PeopleFilters,
  PeoplePage,
  Person,
  PersonConversation,
  PersonConversationPage,
  PersonStats,
  PersonSummary,
  PersonWidget,
  PersonWidgetUsage,
  UpdatePersonRequest,
} from './types';

// Under the chat's ['conversations'] key, so a person's conversations refresh with the Inbox.
const peopleKeys = {
  all: ['people'] as const,
  lists: ['people', 'list'] as const,
  list: (filters: PeopleFilters) => ['people', 'list', filters] as const,
  count: (filters: PeopleFilters) => ['people', 'count', filters] as const,
  attributes: ['people', 'attributes'] as const,
  values: (key: string) => ['people', 'attributes', key, 'values'] as const,
  segments: ['segments'] as const,
  detail: (id: Guid) => ['people', id] as const,
  conversations: (id: Guid) => ['conversations', 'person', id] as const,
};

const PEOPLE_PAGE_SIZE = 50;
const PERSON_CONVERSATIONS_PAGE_SIZE = 25;

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function num(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function toWidget(value: unknown): PersonWidget {
  const dto = isRecord(value) ? value : {};
  return { id: String(dto.id ?? ''), name: text(dto.name) ?? 'Widget' };
}

const TYPES: readonly AttributeType[] = ['string', 'number', 'boolean', 'date'];
const SOURCES: readonly AttributeSource[] = ['browser', 'verified', 'server', 'admin'];

function attributeType(value: unknown): AttributeType {
  return TYPES.includes(value as AttributeType) ? (value as AttributeType) : 'string';
}

function attributeValue(value: unknown): AttributeValue | null {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' ? value : null;
}

function attributeValues(value: unknown): Record<string, AttributeValue> {
  const values: Record<string, AttributeValue> = {};
  if (isRecord(value)) {
    for (const [key, item] of Object.entries(value)) {
      const parsed = attributeValue(item);
      if (parsed !== null) {
        values[key] = parsed;
      }
    }
  }
  return values;
}

function toPersonAttribute(value: unknown): PersonAttribute | null {
  const dto = isRecord(value) ? value : {};
  const parsed = attributeValue(dto.value);
  if (typeof dto.key !== 'string' || parsed === null) {
    return null;
  }
  return {
    key: dto.key,
    label: text(dto.label) ?? dto.key,
    type: attributeType(dto.type),
    value: parsed,
    source: SOURCES.includes(dto.source as AttributeSource) ? (dto.source as AttributeSource) : 'verified',
    updatedAt: String(dto.updatedAt ?? ''),
    visible: dto.visible !== false,
  };
}

function toContactAttribute(value: unknown): ContactAttribute {
  const dto = isRecord(value) ? value : {};
  return {
    key: String(dto.key ?? ''),
    label: text(dto.label) ?? String(dto.key ?? ''),
    type: attributeType(dto.type),
    firstSeenAt: String(dto.firstSeenAt ?? ''),
    lastSeenAt: String(dto.lastSeenAt ?? ''),
    contacts: num(dto.contacts),
    visible: dto.visible !== false,
    exposeToAgent: dto.exposeToAgent === true,
    trust: dto.trust === 'verified' || dto.trust === 'server' ? dto.trust : 'any',
  };
}

function toFilterAttribute(value: unknown): FilterAttribute {
  const dto = isRecord(value) ? value : {};
  return {
    key: String(dto.key ?? ''),
    label: text(dto.label) ?? String(dto.key ?? ''),
    type: dto.type === 'widget' ? 'widget' : attributeType(dto.type),
    builtIn: dto.builtIn === true,
    ops: asArray<unknown>(dto.ops).filter((op): op is string => typeof op === 'string'),
  };
}

function toSegment(value: unknown): Segment {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    name: text(dto.name) ?? 'Segment',
    description: text(dto.description),
    filter: asArray<unknown>(dto.filter).filter((item): item is ContactCondition => isRecord(item) && typeof item.attr === 'string' && typeof item.op === 'string'),
    color: text(dto.color) ?? 'gray',
    icon: text(dto.icon) ?? 'users',
    shared: dto.shared !== false,
    createdByName: text(dto.createdByName),
    mine: dto.mine === true,
    count: num(dto.count),
  };
}

function peopleQuery(filters: PeopleFilters): Record<string, string | boolean | undefined> {
  return {
    q: filters.q || undefined,
    widgetId: filters.widgetId || undefined,
    identified: filters.identified || undefined,
    filter: filters.conditions && filters.conditions.length > 0 ? encodeFilter(filters.conditions) : undefined,
    segmentId: filters.segmentId || undefined,
    all: filters.all ? true : undefined,
  };
}

function toPersonSummary(value: unknown): PersonSummary {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    name: text(dto.name) ?? 'Visitor',
    email: text(dto.email),
    externalId: text(dto.externalId),
    identified: dto.identified === true,
    firstSeenAt: String(dto.firstSeenAt ?? ''),
    lastSeenAt: String(dto.lastSeenAt ?? ''),
    conversationCount: num(dto.conversationCount),
    widgets: asArray<unknown>(dto.widgets).map(toWidget),
    sessionCount: num(dto.sessionCount),
    returning: dto.returning === true,
    language: text(dto.language),
    lastPageUrl: text(dto.lastPageUrl),
    attributes: attributeValues(dto.attributes),
  };
}

function toStats(value: unknown, lastSeenAt: string): PersonStats {
  const dto = isRecord(value) ? value : {};
  return {
    conversations: num(dto.conversations),
    handovers: num(dto.handovers),
    averageRating: typeof dto.averageRating === 'number' ? dto.averageRating : null,
    ratings: num(dto.ratings),
    lastSeenAt: text(dto.lastSeenAt) ?? lastSeenAt,
  };
}

function toPerson(value: unknown): Person {
  const dto = isRecord(value) ? value : {};
  const attributes = asArray<unknown>(dto.attributes)
    .map(toPersonAttribute)
    .filter((item): item is PersonAttribute => item !== null);
  const lastSeenAt = String(dto.lastSeenAt ?? '');
  return {
    id: String(dto.id ?? ''),
    name: text(dto.name) ?? 'Visitor',
    savedName: text(dto.savedName),
    email: text(dto.email),
    externalId: text(dto.externalId),
    identified: dto.identified === true,
    notes: text(dto.notes),
    attributes,
    firstSeenAt: String(dto.firstSeenAt ?? ''),
    lastSeenAt,
    widgets: asArray<unknown>(dto.widgets).map((item): PersonWidgetUsage => {
      const row = isRecord(item) ? item : {};
      return { ...toWidget(row), firstSeenAt: String(row.firstSeenAt ?? ''), lastSeenAt: String(row.lastSeenAt ?? '') };
    }),
    stats: toStats(dto.stats, lastSeenAt),
    sessionCount: num(dto.sessionCount),
    returning: dto.returning === true,
    lastPageUrl: text(dto.lastPageUrl),
    lastReferrer: text(dto.lastReferrer),
    language: text(dto.language),
  };
}

function toConversation(value: unknown): PersonConversation {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    title: text(dto.title) ?? 'Conversation',
    status: text(dto.status) ?? 'ai',
    widgetId: text(dto.widgetId),
    updatedAt: String(dto.updatedAt ?? ''),
    messageCount: num(dto.messageCount),
    preview: text(dto.preview),
    handoverStatus: text(dto.handoverStatus),
    rating: typeof dto.rating === 'number' ? dto.rating : null,
  };
}

/** Null for portal conversations. */
export function toConversationContact(value: unknown): ConversationContact | null {
  if (!isRecord(value) || typeof value.id !== 'string') {
    return null;
  }
  return { id: value.id, name: text(value.name) ?? 'Visitor', email: text(value.email), identified: value.identified === true };
}

const personPath = (id: Guid) => `/api/people/${encodeURIComponent(id)}`;

const peopleApi = {
  list: async (filters: PeopleFilters, cursor: string | null, signal?: AbortSignal): Promise<PeoplePage> => {
    const dto = await request<unknown>('/api/people', {
      query: { ...peopleQuery(filters), cursor, limit: PEOPLE_PAGE_SIZE },
      ...(signal ? { signal } : {}),
    });
    const page = isRecord(dto) ? dto : {};
    return { items: asArray<unknown>(page.items).map(toPersonSummary), nextCursor: text(page.nextCursor) };
  },
  count: async (filters: PeopleFilters, signal?: AbortSignal): Promise<number> => {
    const dto = await request<unknown>('/api/people/count', { query: peopleQuery(filters), ...(signal ? { signal } : {}) });
    return num(isRecord(dto) ? dto.count : 0);
  },
  attributes: async (signal?: AbortSignal): Promise<AttributeRegistry> => {
    const dto = await request<unknown>('/api/people/attributes', signal ? { signal } : {});
    const body = isRecord(dto) ? dto : {};
    const ops: Record<string, string[]> = {};
    if (isRecord(body.ops)) {
      for (const [type, list] of Object.entries(body.ops)) {
        ops[type] = asArray<unknown>(list).filter((op): op is string => typeof op === 'string');
      }
    }
    return { items: asArray<unknown>(body.items).map(toContactAttribute), builtIn: asArray<unknown>(body.builtIn).map(toFilterAttribute), ops };
  },
  values: async (key: string, signal?: AbortSignal): Promise<string[]> => {
    const dto = await request<unknown>(`/api/people/attributes/${encodeURIComponent(key)}/values`, signal ? { signal } : {});
    return asArray<unknown>(dto).flatMap((item) => (isRecord(item) && typeof item.value === 'string' ? [item.value] : []));
  },
  updateAttribute: async (key: string, body: Partial<Pick<ContactAttribute, 'label' | 'visible' | 'exposeToAgent' | 'trust'>>) =>
    toContactAttribute(await request<unknown>(`/api/people/attributes/${encodeURIComponent(key)}`, { method: 'PATCH', body })),
  deleteAttribute: (key: string) => request<unknown>(`/api/people/attributes/${encodeURIComponent(key)}`, { method: 'DELETE', body: { confirm: true } }),
  setAttributes: (id: Guid, attributes: Record<string, AttributeValue | null>) =>
    request<unknown>(`${personPath(id)}/attributes`, { method: 'PATCH', body: { attributes } }),
  segments: async (signal?: AbortSignal): Promise<Segment[]> => asArray<unknown>(await request<unknown>('/api/segments', signal ? { signal } : {})).map(toSegment),
  createSegment: async (body: SaveSegmentRequest) => toSegment(await request<unknown>('/api/segments', { method: 'POST', body })),
  updateSegment: async (id: Guid, body: SaveSegmentRequest) => toSegment(await request<unknown>(`/api/segments/${encodeURIComponent(id)}`, { method: 'PATCH', body })),
  deleteSegment: (id: Guid) => request<unknown>(`/api/segments/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  get: async (id: Guid, signal?: AbortSignal) => toPerson(await request<unknown>(personPath(id), signal ? { signal } : {})),
  update: async (id: Guid, body: UpdatePersonRequest) => toPerson(await request<unknown>(personPath(id), { method: 'PATCH', body })),
  // Erasure can't be undone, so the API requires an explicit confirmation in the body.
  erase: (id: Guid) => request<unknown>(personPath(id), { method: 'DELETE', body: { confirm: true } }),
  export: (id: Guid) => request<unknown>(`${personPath(id)}/export`),
  conversations: async (id: Guid, cursor: string | null, signal?: AbortSignal): Promise<PersonConversationPage> => {
    const dto = await request<unknown>('/api/conversations/search', {
      query: { contactId: id, includeTests: true, sort: 'updated', cursor, limit: PERSON_CONVERSATIONS_PAGE_SIZE },
      ...(signal ? { signal } : {}),
    });
    const page = isRecord(dto) ? dto : {};
    return { items: asArray<unknown>(page.items).map(toConversation), nextCursor: text(page.nextCursor) };
  },
};

export function usePeople(filters: PeopleFilters, enabled = true) {
  return useInfiniteQuery({
    enabled,
    queryKey: peopleKeys.list(filters),
    queryFn: ({ pageParam, signal }) => peopleApi.list(filters, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    placeholderData: keepPreviousData,
    staleTime: 15_000,
  });
}

export function usePerson(id: Guid) {
  return useQuery({ queryKey: peopleKeys.detail(id), queryFn: ({ signal }) => peopleApi.get(id, signal), enabled: id !== '' });
}

export function usePersonConversations(id: Guid) {
  return useInfiniteQuery({
    queryKey: peopleKeys.conversations(id),
    queryFn: ({ pageParam, signal }) => peopleApi.conversations(id, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
    enabled: id !== '',
  });
}

export function useUpdatePerson(id: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdatePersonRequest) => peopleApi.update(id, body),
    onSuccess: (person) => {
      queryClient.setQueryData(peopleKeys.detail(id), person);
      void queryClient.invalidateQueries({ queryKey: peopleKeys.lists });
      // Conversation lists show the person's name.
      void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
    },
  });
}

export function useErasePerson(id: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => peopleApi.erase(id),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: peopleKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: peopleKeys.all });
      void queryClient.invalidateQueries({ queryKey: conversationKeys.all });
    },
  });
}

export function useExportPerson(id: Guid) {
  return useMutation({ mutationFn: () => downloadPersonExport({ id }) });
}

async function downloadPersonExport(person: Pick<Person, 'id'>): Promise<void> {
  const data = await peopleApi.export(person.id);
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `person-${person.id}.json`;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function usePeopleCount(filters: PeopleFilters, enabled = true) {
  return useQuery({
    enabled,
    queryKey: peopleKeys.count(filters),
    queryFn: ({ signal }) => peopleApi.count(filters, signal),
    placeholderData: keepPreviousData,
    staleTime: 15_000,
  });
}

export function useAttributeRegistry() {
  return useQuery({ queryKey: peopleKeys.attributes, queryFn: ({ signal }) => peopleApi.attributes(signal), staleTime: 60_000 });
}

export function useAttributeValues(key: string, enabled: boolean) {
  return useQuery({ queryKey: peopleKeys.values(key), queryFn: ({ signal }) => peopleApi.values(key, signal), enabled: enabled && key !== '', staleTime: 60_000 });
}

/** Also used by the Inbox's segment filter. */
export function useSegments() {
  return useQuery({ queryKey: peopleKeys.segments, queryFn: ({ signal }) => peopleApi.segments(signal), staleTime: 30_000 });
}

export function useSaveSegment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id?: Guid; body: SaveSegmentRequest }) => (id ? peopleApi.updateSegment(id, body) : peopleApi.createSegment(body)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: peopleKeys.segments }),
  });
}

export function useDeleteSegment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: Guid) => peopleApi.deleteSegment(id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: peopleKeys.segments }),
  });
}

export function useUpdateAttribute() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, body }: { key: string; body: Partial<Pick<ContactAttribute, 'label' | 'visible' | 'exposeToAgent' | 'trust'>> }) => peopleApi.updateAttribute(key, body),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: peopleKeys.all }),
  });
}

export function useDeleteAttribute() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => peopleApi.deleteAttribute(key),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: peopleKeys.all });
      void queryClient.invalidateQueries({ queryKey: peopleKeys.segments });
    },
  });
}

/** null clears a value. */
export function useSetPersonAttributes(id: Guid) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (attributes: Record<string, AttributeValue | null>) => peopleApi.setAttributes(id, attributes),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: peopleKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: peopleKeys.lists });
    },
  });
}
