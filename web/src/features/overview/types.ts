import type { Guid, IsoDateTime } from '../../lib/types';

/** Portal and website conversations together. */
export interface OverviewDto {
  days: number;
  from: IsoDateTime;
  /** Started since `from`. */
  conversations: number;
  websiteConversations: number;
  /** Started in the same number of days before `from`, for the trend. */
  previousConversations: number;
  /** Nobody asked for a person, or the visitor went back to the assistant before anyone joined. */
  answeredByAi: number;
  handedToPerson: number;
  /** Conversation ratings (1 to 5) since `from`; null without any. */
  averageRating: number | null;
  ratings: number;
  widgets: WidgetActivityDto[];
}

export interface WidgetActivityDto {
  widgetId: Guid;
  /** Started since `from`. */
  conversations: number;
  /** Null before the first conversation. */
  lastConversationAt: IsoDateTime | null;
}
