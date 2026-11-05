import type { Guid, IsoDateTime } from '../../lib/types';
import type { ConversationChannel, ConversationDto, ConversationStatus, ConversationSummaryDto } from '../chat/types';
import type { ConversationLabel } from '../labels/types';
import type { ConversationContact } from '../people/types';

export interface InboxConversationDto extends ConversationSummaryDto {
  widgetId: Guid | null;
  /** Truncated text of the latest message. */
  preview: string | null;
  /** Of the latest request for a person; expired when nobody joined in time. */
  handoverStatus: HandoverStatus | null;
  pinned: boolean;
  labels: ConversationLabel[];
}

export interface InboxConversationDetail extends ConversationDto {
  widgetId: Guid | null;
  visitorId: Guid | null;
  pinned: boolean;
  contact: ConversationContact | null;
  /** Set for playground conversations. */
  createdBy: { id: string; name: string } | null;
  assignee: ConversationAssignee | null;
}

/** id is null for a name from Slack or email. */
export interface ConversationAssignee {
  id: string | null;
  name: string;
}

/** Anyone who has signed in to the portal. */
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

/** cancelled: the visitor went back to the assistant first; expired: nobody joined in time. */
export type HandoverStatus = 'pending' | 'active' | 'resolved' | 'cancelled' | 'expired';

export type HandoverSource = 'agent' | 'visitor' | 'staff';

export interface HandoverDto {
  id: Guid;
  conversationId: Guid;
  /** portal, email or slack. */
  channel: string;
  status: HandoverStatus;
  source: HandoverSource;
  reason: string | null;
  summary: string | null;
  assignee: string | null;
  requestedAt: IsoDateTime;
  acceptedAt: IsoDateTime | null;
  resolvedAt: IsoDateTime | null;
  /** Why delivery by email or Slack failed. */
  lastError: string | null;
}

export interface InboxFilters {
  /** Empty for every status. */
  status: ConversationStatus | '';
  /** Empty for every widget. */
  widgetId: string;
}

export type InboxView = 'mine' | 'waiting' | 'unassigned' | 'ai' | 'closed' | 'all';

export type ConversationSort = 'updated_desc' | 'updated_asc' | 'created_desc' | 'created_asc';

/** good: 4 or 5 stars; poor: 1 or 2. */
type RatingFilter = '' | 'good' | 'poor';

/** Days back; empty for any time. */
export type PeriodFilter = '' | '7' | '30' | '90';

/** Kept in the URL so a view, a search and a conversation can be linked. */
export interface InboxState {
  view: InboxView;
  /** Empty for any label. */
  labelId: string;
  q: string;
  channel: ConversationChannel | '';
  widgetId: string;
  handover: '' | 'yes' | 'no';
  rating: RatingFilter;
  thumbsDown: boolean;
  period: PeriodFilter;
  sort: ConversationSort;
  /** Empty for anyone. */
  contactId: string;
  /** me, none or a staff member's id; empty for anyone. */
  assignee: string;
  pinned: boolean;
  /** Empty for none. */
  segmentId: string;
  conversationId: string | null;
  /** Set when a page without its own list opened one conversation; the list shows only it until the chip is removed. */
  only: string;
  /** False when the URL named no view (e.g. a contact deep link), so no view tab claims the list. */
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
  /** Truncated text of the latest message. */
  preview: string | null;
  /** The first message's passage with the first search word; null without a search or on a title match. */
  match: string | null;
  handoverStatus: HandoverStatus | null;
  /** 1 to 5. */
  rating: number | null;
  /** Answers rated thumbs down. */
  thumbsDown: number;
  pinned: boolean;
  labels: ConversationLabel[];
  contact: ConversationContact | null;
  assignee: ConversationAssignee | null;
  /** A visitor or assistant message came after the user's read position. */
  unread: boolean;
}

export interface ConversationSearchPage {
  items: ConversationSearchItem[];
  /** Null on the last page. */
  nextCursor: string | null;
}

export interface InboxCounts {
  views: Readonly<Record<InboxView, number>>;
  /** Unread per view, for the signed-in user. */
  unread: Readonly<Record<InboxView, number>>;
  pinned: number;
  /** By lower-case label id. */
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

/** Newest first. */
export interface ConversationHistory {
  feedback: ConversationFeedbackItem[];
  handovers: HandoverDto[];
  labels: ConversationLabel[];
}
