import type { Guid, IsoDateTime } from '../../lib/types';
import type { ConversationChannel, ConversationStatus } from '../chat/types';

export interface RecentConversation {
  id: Guid;
  title: string;
  channel: ConversationChannel;
  status: ConversationStatus;
  widgetId: Guid | null;
  updatedAt: IsoDateTime;
}
