import type { Guid, IsoDateTime } from '../../lib/types';
import type { ConversationChannel, ConversationDto, ConversationStatus, ConversationSummaryDto } from '../chat/types';
import type { ConversationLabel } from '../labels/types';
import type { ConversationContact } from '../people/types';

export interface InboxConversationDto extends ConversationSummaryDto {
  widgetId: Guid | null;
  preview: string | null;
  handoverStatus: HandoverStatus | null;
  pinned: boolean;
  labels: ConversationLabel[];
}

export interface InboxConversationDetail extends ConversationDto {
  widgetId: Guid | null;
  visitorId: Guid | null;
  pinned: boolean;
  contact: ConversationContact | null;
  createdBy: { id: string; name: string } | null;
  assignee: ConversationAssignee | null;
}

export interface ConversationAssignee {
  id: string | null;
  name: string;
}

export interface StaffMember {
  id: string;
  name: string;
  email: string | null;
  role: string;
}

export interface Segment {
  id: string;
  name: string;
}

export type HandoverStatus = 'pending' | 'active' | 'resolved' | 'cancelled' | 'expired';

export type HandoverSource = 'agent' | 'visitor' | 'staff';

export interface HandoverDto {
  id: Guid;
  conversationId: Guid;
  channel: string;
  status: HandoverStatus;
  source: HandoverSource;
  reason: string | null;
  summary: string | null;
  assignee: string | null;
  requestedAt: IsoDateTime;
  acceptedAt: IsoDateTime | null;
  resolvedAt: IsoDateTime | null;
  lastError: string | null;
}

export interface InboxFilters {
  status: ConversationStatus | '';
  widgetId: string;
}

export type InboxView = 'mine' | 'waiting' | 'unassigned' | 'ai' | 'closed' | 'all';

export type ConversationSort = 'updated_desc' | 'updated_asc' | 'created_desc' | 'created_asc';

type RatingFilter = '' | 'good' | 'poor';

export type PeriodFilter = '' | '7' | '30' | '90';

export interface InboxState {
  view: InboxView;
  labelId: string;
  q: string;
  channel: ConversationChannel | '';
  widgetId: string;
  handover: '' | 'yes' | 'no';
  rating: RatingFilter;
  thumbsDown: boolean;
  period: PeriodFilter;
  sort: ConversationSort;
  contactId: string;
  assignee: string;
  pinned: boolean;
  segmentId: string;
  conversationId: string | null;
  only: string;
  viewChosen: boolean;
}

export interface ConversationSearchItem {
  id: Guid;
  title: string;
  channel: ConversationChannel;
  status: ConversationStatus;
  agentId: Guid | null;
  widgetId: Guid | null;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  messageCount: number;
  preview: string | null;
  match: string | null;
  handoverStatus: HandoverStatus | null;
  rating: number | null;
  thumbsDown: number;
  pinned: boolean;
  labels: ConversationLabel[];
  contact: ConversationContact | null;
  assignee: ConversationAssignee | null;
  unread: boolean;
}

export interface ConversationSearchPage {
  items: ConversationSearchItem[];
  nextCursor: string | null;
}

export interface InboxCounts {
  views: Readonly<Record<InboxView, number>>;
  unread: Readonly<Record<InboxView, number>>;
  pinned: number;
  labels: ReadonlyMap<string, number>;
}

export interface ConversationFeedbackItem {
  id: Guid;
  messageId: Guid | null;
  kind: 'answer' | 'conversation' | 'agent';
  rating: number;
  comment: string | null;
  reasons: string[];
  author: 'visitor' | 'staff';
  createdAt: IsoDateTime;
}

export interface ConversationHistory {
  feedback: ConversationFeedbackItem[];
  handovers: HandoverDto[];
  labels: ConversationLabel[];
}
