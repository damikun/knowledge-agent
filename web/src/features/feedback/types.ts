import type { Guid, IsoDateTime } from '../../lib/types';

/** answer: thumbs (1 or -1); conversation: stars (1 to 5); agent: noted by the assistant (-1, 0 or 1). */
export type FeedbackKind = 'answer' | 'conversation' | 'agent';

type FeedbackAuthor = 'visitor' | 'staff';

/** A null widget is the portal chat. */
export interface FeedbackSummaryDto {
  widgetId: Guid | null;
  agentId: Guid | null;
  answers: number;
  positive: number;
  negative: number;
  /** 0 to 1; null without rated answers. */
  positiveRatio: number | null;
  ratings: number;
  /** 1 to 5; null without rated conversations. */
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
  /** Truncated text of the rated answer. */
  messageText: string | null;
}

export type FeedbackDays = 7 | 30 | 90;

export interface FeedbackFilters {
  days: FeedbackDays;
  widgetId: string;
  agentId: string;
  kind: FeedbackKind | '';
  /** "true" for thumbs up, 4 or 5 stars and positive notes. */
  positive: 'true' | 'false' | '';
}
