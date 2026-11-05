import type { Guid, IsoDateTime } from '../../lib/types';

export interface PersonWidget {
  id: Guid;
  name: string;
}

/** `name` falls back to e.g. "Visitor 4F2A" for anonymous people. */
export interface PersonSummary {
  id: Guid;
  name: string;
  email: string | null;
  externalId: string | null;
  identified: boolean;
  firstSeenAt: IsoDateTime;
  lastSeenAt: IsoDateTime;
  conversationCount: number;
  widgets: PersonWidget[];
  sessionCount: number;
  returning: boolean;
  language: string | null;
  lastPageUrl: string | null;
  /** By key. */
  attributes: Record<string, AttributeValue>;
}

export type AttributeValue = string | number | boolean;
export type AttributeType = 'string' | 'number' | 'boolean' | 'date';
export type AttributeSource = 'browser' | 'verified' | 'server' | 'admin';
export type AttributeTrust = 'any' | 'verified' | 'server';

export interface PersonAttribute {
  key: string;
  label: string;
  type: AttributeType;
  value: AttributeValue;
  source: AttributeSource;
  updatedAt: IsoDateTime;
  visible: boolean;
}

export interface ContactAttribute {
  key: string;
  label: string;
  type: AttributeType;
  firstSeenAt: IsoDateTime;
  lastSeenAt: IsoDateTime;
  contacts: number;
  visible: boolean;
  exposeToAgent: boolean;
  trust: AttributeTrust;
}

/** Built-in attributes are computed by the server. */
export interface FilterAttribute {
  key: string;
  label: string;
  /** 'widget' for the widget a contact used. */
  type: AttributeType | 'widget';
  builtIn: boolean;
  ops: string[];
}

export interface AttributeRegistry {
  items: ContactAttribute[];
  builtIn: FilterAttribute[];
  ops: Record<string, string[]>;
}

/** A filter matches when all conditions do. */
export interface ContactCondition {
  attr: string;
  op: string;
  value?: string | number | boolean | [number, number];
}

export interface Segment {
  id: Guid;
  name: string;
  description: string | null;
  filter: ContactCondition[];
  color: string;
  icon: string;
  shared: boolean;
  createdByName: string | null;
  mine: boolean;
  count: number;
}

export interface SaveSegmentRequest {
  name?: string;
  description?: string;
  filter?: ContactCondition[];
  color?: string;
  shared?: boolean;
}

export interface PeoplePage {
  items: PersonSummary[];
  nextCursor: string | null;
}

export interface PeopleFilters {
  q: string;
  widgetId: string;
  /** '' for everyone. */
  identified: '' | 'true' | 'false';
  conditions?: ContactCondition[];
  /** '' for none. */
  segmentId?: string;
  /** Every contact, including those who never talked or signed in. */
  all?: boolean;
}

export interface PersonWidgetUsage extends PersonWidget {
  firstSeenAt: IsoDateTime;
  lastSeenAt: IsoDateTime;
}

export interface PersonStats {
  conversations: number;
  handovers: number;
  averageRating: number | null;
  ratings: number;
  lastSeenAt: IsoDateTime;
}

/** `savedName` is what an edit changes. */
export interface Person {
  id: Guid;
  name: string;
  savedName: string | null;
  email: string | null;
  externalId: string | null;
  identified: boolean;
  notes: string | null;
  attributes: PersonAttribute[];
  firstSeenAt: IsoDateTime;
  lastSeenAt: IsoDateTime;
  widgets: PersonWidgetUsage[];
  stats: PersonStats;
  sessionCount: number;
  returning: boolean;
  lastPageUrl: string | null;
  lastReferrer: string | null;
  language: string | null;
}

/** Omitted fields stay; empty ones are cleared. */
export interface UpdatePersonRequest {
  name?: string;
  email?: string;
  notes?: string;
}

export interface PersonConversation {
  id: Guid;
  title: string;
  status: string;
  widgetId: Guid | null;
  updatedAt: IsoDateTime;
  messageCount: number;
  preview: string | null;
  handoverStatus: string | null;
  rating: number | null;
}

export interface PersonConversationPage {
  items: PersonConversation[];
  nextCursor: string | null;
}

export interface ConversationContact {
  id: Guid;
  name: string;
  email: string | null;
  identified: boolean;
}
