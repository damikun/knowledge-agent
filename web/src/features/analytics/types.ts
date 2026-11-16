import type { Guid, IsoDateTime } from '../../lib/types';
import type { LabelColor } from '../labels/types';

export type AnalyticsDays = 7 | 30 | 90;

export interface AnalyticsTotals {
  conversations: number;
  answeredWithoutPerson: number;
  handedToPerson: number;
  ratings: number;
  averageRating: number | null;
  thumbsUp: number;
  thumbsDown: number;
  noSourceAnswers: number;
}

export interface AnalyticsDay {
  date: string;
  conversations: number;
  handedToPerson: number;
  thumbsUp: number;
  thumbsDown: number;
  ratings: number;
  averageRating: number | null;
}

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

export interface AnalyticsLabel {
  labelId: Guid;
  name: string;
  color: LabelColor;
  conversations: number;
  previousConversations: number;
}

export interface AnalyticsSegment {
  segmentId: Guid;
  name: string;
  color: LabelColor;
  contacts: number;
  conversations: number;
  handedToPerson: number;
  handoverRate: number | null;
  ratings: number;
  averageRating: number | null;
}

export interface Analytics {
  days: number;
  from: IsoDateTime;
  to: IsoDateTime;
  totals: AnalyticsTotals;
  daily: AnalyticsDay[];
  widgets: AnalyticsWidget[];
  unanswered: UnansweredQuestion[];
  labels: AnalyticsLabel[];
  segments: AnalyticsSegment[];
}
