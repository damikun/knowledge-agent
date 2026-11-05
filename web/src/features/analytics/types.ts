import type { Guid, IsoDateTime } from '../../lib/types';
import type { LabelColor } from '../labels/types';

export type AnalyticsDays = 7 | 30 | 90;

export interface AnalyticsTotals {
  conversations: number;
  /** Nobody asked for a person, or the visitor went back to the assistant first. */
  answeredWithoutPerson: number;
  handedToPerson: number;
  ratings: number;
  averageRating: number | null;
  thumbsUp: number;
  thumbsDown: number;
  /** Answers after the agent searched the knowledge base and cited nothing. */
  noSourceAnswers: number;
}

/** One UTC day. */
export interface AnalyticsDay {
  /** yyyy-mm-dd */
  date: string;
  conversations: number;
  handedToPerson: number;
  thumbsUp: number;
  thumbsDown: number;
  ratings: number;
  averageRating: number | null;
}

/** widgetId null is the portal chat. */
export interface AnalyticsWidget {
  widgetId: Guid | null;
  conversations: number;
  handedToPerson: number;
  thumbsUp: number;
  thumbsDown: number;
  ratings: number;
  averageRating: number | null;
}

export type UnansweredReason = 'handover' | 'thumbs_down' | 'no_sources';

/** A question the agent could not answer well; the example fields are of the latest time it was asked. */
export interface UnansweredQuestion {
  question: string;
  count: number;
  reasons: UnansweredReason[];
  lastAskedAt: IsoDateTime;
  conversationId: Guid;
  widgetId: Guid | null;
  messageId: Guid | null;
  answer: string | null;
  comment: string | null;
}

/** Conversations that started in the period with the label, and in the period of the same length before it. */
export interface AnalyticsLabel {
  labelId: Guid;
  name: string;
  color: LabelColor;
  conversations: number;
  previousConversations: number;
}

/** A segment's numbers in the period: conversations with people who are in it now. */
export interface AnalyticsSegment {
  segmentId: Guid;
  name: string;
  color: LabelColor;
  contacts: number;
  conversations: number;
  handedToPerson: number;
  /** 0 to 1; null without conversations. */
  handoverRate: number | null;
  ratings: number;
  averageRating: number | null;
}

/** GET /api/analytics */
export interface Analytics {
  days: number;
  from: IsoDateTime;
  to: IsoDateTime;
  totals: AnalyticsTotals;
  daily: AnalyticsDay[];
  widgets: AnalyticsWidget[];
  unanswered: UnansweredQuestion[];
  /** The most used labels of the period, at most ten. */
  labels: AnalyticsLabel[];
  /** Each saved segment, at most twenty. */
  segments: AnalyticsSegment[];
}
