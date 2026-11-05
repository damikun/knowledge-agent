import { useQuery } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../lib/api';
import type { OverviewDto, WidgetActivityDto } from './types';

export const OVERVIEW_DAYS = 7;

// Under the chat's ['conversations'] key, so new conversations refresh the numbers too.
const overviewKey = ['conversations', 'overview', OVERVIEW_DAYS] as const;

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function toWidgetActivity(value: unknown): WidgetActivityDto | null {
  const dto = isRecord(value) ? value : {};
  const widgetId = text(dto.widgetId);
  return widgetId ? { widgetId, conversations: count(dto.conversations), lastConversationAt: text(dto.lastConversationAt) } : null;
}

function toOverview(value: unknown): OverviewDto {
  const dto = isRecord(value) ? value : {};
  return {
    days: count(dto.days) || OVERVIEW_DAYS,
    from: text(dto.from) ?? '',
    conversations: count(dto.conversations),
    websiteConversations: count(dto.websiteConversations),
    previousConversations: count(dto.previousConversations),
    answeredByAi: count(dto.answeredByAi),
    handedToPerson: count(dto.handedToPerson),
    averageRating: typeof dto.averageRating === 'number' ? dto.averageRating : null,
    ratings: count(dto.ratings),
    widgets: asArray<unknown>(dto.widgets).flatMap((item) => toWidgetActivity(item) ?? []),
  };
}

const overviewApi = {
  getOverview: async (signal?: AbortSignal) => toOverview(await request<unknown>('/api/overview', { query: { days: OVERVIEW_DAYS }, signal })),
};

/** Admin only. */
export function useOverview() {
  return useQuery({ queryKey: overviewKey, queryFn: ({ signal }) => overviewApi.getOverview(signal), staleTime: 30_000, refetchInterval: 60_000 });
}
