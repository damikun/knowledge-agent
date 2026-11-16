import { useQuery } from '@tanstack/react-query';
import { call, isRecord } from '../../lib/api';
import { endpoints } from '../../lib/endpoints';
import { conversationItems, isConversationStatus } from '../inbox/api';
import type { RecentConversation } from './types';

const RECENT_CONVERSATIONS = 6;

const homeKeys = {
  recent: ['conversations', 'recent'] as const,
};

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function toRecentConversation(value: unknown): RecentConversation {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    title: typeof dto.title === 'string' ? dto.title : '',
    channel: dto.channel === 'widget' ? 'widget' : 'portal',
    status: isConversationStatus(dto.status) ? dto.status : 'ai',
    widgetId: text(dto.widgetId),
    updatedAt: String(dto.updatedAt ?? ''),
  };
}

const homeApi = {
  listRecent: async (signal?: AbortSignal) =>
    conversationItems(await call(endpoints.conversations.list, { query: { limit: RECENT_CONVERSATIONS }, signal })).map(toRecentConversation),
};

export function useRecentConversations() {
  return useQuery({ queryKey: homeKeys.recent, queryFn: ({ signal }) => homeApi.listRecent(signal), staleTime: 15_000, refetchInterval: 30_000 });
}
