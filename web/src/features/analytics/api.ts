import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asArray, isRecord, request } from '../../lib/api';
import { toLabelColor } from '../labels/api';
import type {
  Analytics,
  AnalyticsDay,
  AnalyticsDays,
  AnalyticsLabel,
  AnalyticsSegment,
  AnalyticsTotals,
  AnalyticsWidget,
  UnansweredQuestion,
  UnansweredReason,
} from './types';

// Under the chat's ['conversations'] key, so new conversations refresh the numbers with the conversation lists.
const analyticsKeys = {
  get: (days: AnalyticsDays) => ['conversations', 'analytics', days] as const,
};

const REASONS: readonly UnansweredReason[] = ['handover', 'thumbs_down', 'no_sources'];

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function average(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function record(value: unknown): Record<string, unknown> {
  return isRecord(value) ? value : {};
}

function toTotals(value: unknown): AnalyticsTotals {
  const dto = record(value);
  return {
    conversations: count(dto.conversations),
    answeredWithoutPerson: count(dto.answeredWithoutPerson),
    handedToPerson: count(dto.handedToPerson),
    ratings: count(dto.ratings),
    averageRating: average(dto.averageRating),
    thumbsUp: count(dto.thumbsUp),
    thumbsDown: count(dto.thumbsDown),
    noSourceAnswers: count(dto.noSourceAnswers),
  };
}

function toDay(value: unknown): AnalyticsDay {
  const dto = record(value);
  return {
    date: String(dto.date ?? ''),
    conversations: count(dto.conversations),
    handedToPerson: count(dto.handedToPerson),
    thumbsUp: count(dto.thumbsUp),
    thumbsDown: count(dto.thumbsDown),
    ratings: count(dto.ratings),
    averageRating: average(dto.averageRating),
  };
}

function toWidget(value: unknown): AnalyticsWidget {
  const dto = record(value);
  return {
    widgetId: text(dto.widgetId),
    conversations: count(dto.conversations),
    handedToPerson: count(dto.handedToPerson),
    thumbsUp: count(dto.thumbsUp),
    thumbsDown: count(dto.thumbsDown),
    ratings: count(dto.ratings),
    averageRating: average(dto.averageRating),
  };
}

function toQuestion(value: unknown): UnansweredQuestion {
  const dto = record(value);
  return {
    question: String(dto.question ?? ''),
    count: count(dto.count),
    reasons: asArray<unknown>(dto.reasons).flatMap((reason) => REASONS.filter((known) => known === reason)),
    lastAskedAt: String(dto.lastAskedAt ?? ''),
    conversationId: String(dto.conversationId ?? ''),
    widgetId: text(dto.widgetId),
    messageId: text(dto.messageId),
    answer: text(dto.answer),
    comment: text(dto.comment),
  };
}

function toLabel(value: unknown): AnalyticsLabel {
  const dto = record(value);
  return {
    labelId: String(dto.labelId ?? ''),
    name: typeof dto.name === 'string' ? dto.name : '',
    color: toLabelColor(dto.color),
    conversations: count(dto.conversations),
    previousConversations: count(dto.previousConversations),
  };
}

function toSegment(value: unknown): AnalyticsSegment {
  const dto = record(value);
  return {
    segmentId: String(dto.segmentId ?? ''),
    name: typeof dto.name === 'string' ? dto.name : '',
    color: toLabelColor(dto.color),
    contacts: count(dto.contacts),
    conversations: count(dto.conversations),
    handedToPerson: count(dto.handedToPerson),
    handoverRate: average(dto.handoverRate),
    ratings: count(dto.ratings),
    averageRating: average(dto.averageRating),
  };
}

function toAnalytics(value: unknown): Analytics {
  const dto = record(value);
  return {
    days: count(dto.days),
    from: String(dto.from ?? ''),
    to: String(dto.to ?? ''),
    totals: toTotals(dto.totals),
    daily: asArray<unknown>(dto.daily).map(toDay),
    widgets: asArray<unknown>(dto.widgets).map(toWidget),
    unanswered: asArray<unknown>(dto.unanswered).map(toQuestion),
    labels: asArray<unknown>(dto.labels).map(toLabel),
    segments: asArray<unknown>(dto.segments).map(toSegment),
  };
}

const analyticsApi = {
  get: async (days: AnalyticsDays, signal?: AbortSignal) => toAnalytics(await request<unknown>('/api/analytics', { query: { days }, signal })),
};

export function useAnalytics(days: AnalyticsDays) {
  return useQuery({
    queryKey: analyticsKeys.get(days),
    queryFn: ({ signal }) => analyticsApi.get(days, signal),
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });
}
