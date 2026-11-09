import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { asArray, asStrings, isRecord, request } from '../../lib/api';
import type { FeedbackDays, FeedbackDto, FeedbackFilters, FeedbackKind, FeedbackSummaryDto } from './types';

const feedbackKeys = {
  summary: (days: FeedbackDays) => ['feedback', 'summary', days] as const,
  list: (filters: FeedbackFilters) => ['feedback', 'list', filters] as const,
};

const FEEDBACK_PAGE_SIZE = 100;

const KINDS: readonly FeedbackKind[] = ['answer', 'conversation', 'agent'];

function text(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function ratio(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function toSummary(value: unknown): FeedbackSummaryDto {
  const dto = isRecord(value) ? value : {};
  return {
    widgetId: text(dto.widgetId),
    agentId: text(dto.agentId),
    answers: count(dto.answers),
    positive: count(dto.positive),
    negative: count(dto.negative),
    positiveRatio: ratio(dto.positiveRatio),
    ratings: count(dto.ratings),
    averageRating: ratio(dto.averageRating),
    agentRecorded: count(dto.agentRecorded),
  };
}

function toFeedback(value: unknown): FeedbackDto {
  const dto = isRecord(value) ? value : {};
  return {
    id: String(dto.id ?? ''),
    conversationId: String(dto.conversationId ?? ''),
    messageId: text(dto.messageId),
    kind: KINDS.find((kind) => kind === dto.kind) ?? 'answer',
    rating: count(dto.rating),
    comment: text(dto.comment),
    reasons: asStrings(dto.reasons),
    author: dto.author === 'staff' || dto.author === 'agent' ? dto.author : 'visitor',
    widgetId: text(dto.widgetId),
    agentId: text(dto.agentId),
    traceId: text(dto.traceId),
    createdAt: String(dto.createdAt ?? ''),
    conversationTitle: text(dto.conversationTitle),
    messageText: text(dto.messageText),
  };
}

const feedbackApi = {
  summary: async (days: FeedbackDays, signal?: AbortSignal) =>
    asArray<unknown>(await request<unknown>('/api/feedback/summary', { query: { days }, signal })).map(toSummary),
  list: async (filters: FeedbackFilters, signal?: AbortSignal) =>
    asArray<unknown>(
      await request<unknown>('/api/feedback', {
        query: {
          widgetId: filters.widgetId,
          agentId: filters.agentId,
          kind: filters.kind,
          positive: filters.positive,
          days: filters.days,
          take: FEEDBACK_PAGE_SIZE,
        },
        signal,
      }),
    ).map(toFeedback),
};

export function useFeedbackSummary(days: FeedbackDays) {
  return useQuery({
    queryKey: feedbackKeys.summary(days),
    queryFn: ({ signal }) => feedbackApi.summary(days, signal),
    placeholderData: keepPreviousData,
  });
}

export function useFeedback(filters: FeedbackFilters) {
  return useQuery({
    queryKey: feedbackKeys.list(filters),
    queryFn: ({ signal }) => feedbackApi.list(filters, signal),
    placeholderData: keepPreviousData,
  });
}
