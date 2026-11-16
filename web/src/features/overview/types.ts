import type { Guid, IsoDateTime } from '../../lib/types';

export interface OverviewDto {
  days: number;
  from: IsoDateTime;
  conversations: number;
  websiteConversations: number;
  previousConversations: number;
  answeredByAi: number;
  handedToPerson: number;
  averageRating: number | null;
  ratings: number;
  widgets: WidgetActivityDto[];
}

export interface WidgetActivityDto {
  widgetId: Guid;
  conversations: number;
  lastConversationAt: IsoDateTime | null;
}
