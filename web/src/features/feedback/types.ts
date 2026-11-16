import type { Guid, IsoDateTime } from '../../lib/types';

export type FeedbackKind = 'answer' | 'conversation' | 'agent';

type FeedbackAuthor = 'visitor' | 'staff' | 'agent';

export interface FeedbackSummaryDto {
  widgetId: Guid | null;
  agentId: Guid | null;
  answers: number;
  positive: number;
  negative: number;
  positiveRatio: number | null;
  ratings: number;
  averageRating: number | null;
  agentRecorded: number;
}

export interface FeedbackDto {
  id: Guid;
  conversationId: Guid;
  messageId: Guid | null;
  kind: FeedbackKind;
  rating: number;
  comment: string | null;
  reasons: string[];
  author: FeedbackAuthor;
  widgetId: Guid | null;
  agentId: Guid | null;
  traceId: string | null;
  createdAt: IsoDateTime;
  conversationTitle: string | null;
  messageText: string | null;
}

export type FeedbackDays = 7 | 30 | 90;

export interface FeedbackFilters {
  days: FeedbackDays;
  widgetId: string;
  agentId: string;
  kind: FeedbackKind | '';
  positive: 'true' | 'false' | '';
}
